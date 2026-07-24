using System.Globalization;
using System.Text.Json;

namespace PAXSMResearcherConsole;

internal sealed class ProbeMetricValue
{
    public string MetricId { get; init; } = "";
    public string MetricName { get; init; } = "";
    public string Unit { get; init; } = "";
    public double Value { get; init; }
    public bool Valid { get; init; }
}

internal sealed class ProbeMetricBlock
{
    public string BlockId { get; init; } = "";
    public string TargetDimension { get; init; } = "";
    public string FilePath { get; init; } = "";
    public IReadOnlyDictionary<string, ProbeMetricValue> Metrics { get; init; } =
        new Dictionary<string, ProbeMetricValue>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ProbeCalibrationSnapshot
{
    public string ParticipantId { get; init; } = "";
    public string RunDirectory { get; init; } = "";
    public IReadOnlyDictionary<string, ProbeMetricBlock> Blocks { get; init; } =
        new Dictionary<string, ProbeMetricBlock>(StringComparer.OrdinalIgnoreCase);

    public bool HasBaseline => Blocks.ContainsKey("baseline");
}

internal sealed class ProbeMetricCandidate
{
    public string MetricId { get; init; } = "";
    public string MetricName { get; init; } = "";
    public string Unit { get; init; } = "";
    public double BaselineValue { get; init; }
    public double CalibrationValue { get; init; }
    public double Delta { get; init; }
    public string ObservedDirection => Delta >= 0d ? "Higher than baseline" : "Lower than baseline";
    public int SourceParticipantCount { get; init; } = 1;
    public double DirectionAgreement { get; init; } = 1d;
    public string EvidenceScope { get; init; } = "participant calibration";
}

internal sealed class ProbeCalibrationCohort
{
    public IReadOnlyList<ProbeCalibrationSnapshot> Participants { get; init; } =
        Array.Empty<ProbeCalibrationSnapshot>();
}

/// <summary>
/// A frozen provenance entry in a reusable Probe Plugin. It records exactly which
/// completed calibration run contributed to the task-level direction summary.
/// </summary>
internal sealed class ProbeCalibrationSource
{
    public string ParticipantId { get; set; } = "";
    public string RunDirectory { get; set; } = "";
    public string BaselineMetricsPath { get; set; } = "";
    public string MentalMetricsPath { get; set; } = "";
    public string PhysicalMetricsPath { get; set; } = "";
}

internal sealed class ProbeFeatureRule
{
    public string MetricId { get; set; } = "";
    public string MetricName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string ExpectedDirection { get; set; } = "higher";
    public double CalibrationBaselineValue { get; set; }
    public double CalibrationConditionValue { get; set; }
    public double CalibrationDelta { get; set; }
}

internal sealed class ProbeDimensionRuleCard
{
    public string DimensionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CalibrationBlockId { get; set; } = "";
    public string CalibrationBlockName { get; set; } = "";
    public string SourceParticipantId { get; set; } = "";
    public string CalibrationRunDirectory { get; set; } = "";
    public string BaselineMetricsPath { get; set; } = "";
    public string ConditionMetricsPath { get; set; } = "";
    public string Scope { get; set; } = "provisional_participant_calibration";
    public string CreatedAtUtc { get; set; } = "";
    public List<ProbeFeatureRule> Features { get; set; } = new();
}

internal sealed class ProbeRuleCardSet
{
    public string SchemaVersion { get; set; } = "CAREXR_ProbePlugin_v1";
    public string PluginId { get; set; } = Guid.NewGuid().ToString("N");
    public string PluginName { get; set; } = "Untitled task-probe plugin";
    public string TaskFamily { get; set; } = "XR target-selection workload task";
    public int CalibrationParticipantCount { get; set; }
    public string PluginPurpose { get; set; } =
        "Reusable task-probe definition for Evidence Matrix Y-axis matching.";
    public string UpdatedAtUtc { get; set; } = "";
    public string BoundaryNote { get; set; } =
        "Rule cards describe task-probe evidence. They are not cognitive-state labels and must be checked against calibration data.";
    public List<ProbeCalibrationSource> CalibrationSources { get; set; } = new();
    public List<ProbeDimensionRuleCard> Dimensions { get; set; } = new();

    public ProbeDimensionRuleCard? Find(string dimensionId) => Dimensions.FirstOrDefault(card =>
        card.DimensionId.Equals(dimensionId, StringComparison.OrdinalIgnoreCase));
}

internal sealed class ActiveProbePluginSelection
{
    public string SchemaVersion { get; set; } = "CAREXR_ActiveProbePlugin_v1";
    public string PluginFileName { get; set; } = "";
    public string SelectedAtUtc { get; set; } = "";
}

internal sealed class ProbePluginFile
{
    public string FilePath { get; init; } = "";
    public ProbeRuleCardSet Plugin { get; init; } = new();
    public bool IsActive { get; init; }
}

internal sealed record ProbeDimensionDefinition(
    string Id,
    string DisplayName,
    string CalibrationBlockId,
    string CalibrationBlockName);

internal static class ProbeDimensionCatalog
{
    public static readonly ProbeDimensionDefinition Mental = new(
        "mental", "Mental Demand", "cognitive_heavy", "Mental-demand calibration");
    public static readonly ProbeDimensionDefinition Physical = new(
        "physical", "Physical Demand", "physical_heavy", "Physical-demand calibration");
    public static readonly ProbeDimensionDefinition Temporal = new(
        "temporal", "Temporal Demand", "temporal_heavy", "Temporal-demand calibration");

    public static readonly IReadOnlyList<ProbeDimensionDefinition> All = new[] { Mental, Physical, Temporal };

    public static ProbeDimensionDefinition? FromItemDimension(string value)
    {
        string normalized = Normalize(value);
        if (normalized.Contains("mental") || normalized.Contains("cognitive"))
            return Mental;
        if (normalized.Contains("physical"))
            return Physical;
        if (normalized.Contains("temporal") || normalized.Contains("timepressure"))
            return Temporal;
        return null;
    }

    public static ProbeDimensionDefinition? FromId(string value) =>
        All.FirstOrDefault(item => item.Id.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string value) =>
        new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

internal sealed class ProbeCalibrationReader
{
    public ProbeCalibrationSnapshot? LoadLatest(DataSnapshot snapshot, ResearchSession session)
    {
        IEnumerable<RunManifestRecord> candidateRuns = snapshot.Runs
            .Where(run => string.Equals(run.SceneName, SceneDefinitions.Workload.SceneName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.ConfiguredAtUtc);

        foreach (RunManifestRecord run in candidateRuns)
        {
            Dictionary<string, ProbeMetricBlock> blocks = ReadMetricBlocks(run.RunDirectory, session.ParticipantId);
            if (blocks.ContainsKey("baseline") &&
                (blocks.ContainsKey(ProbeDimensionCatalog.Mental.CalibrationBlockId) ||
                 blocks.ContainsKey(ProbeDimensionCatalog.Physical.CalibrationBlockId) ||
                 blocks.ContainsKey(ProbeDimensionCatalog.Temporal.CalibrationBlockId)))
            {
                return new ProbeCalibrationSnapshot
                {
                    ParticipantId = session.ParticipantId,
                    RunDirectory = run.RunDirectory,
                    Blocks = blocks
                };
            }
        }

        return null;
    }

    public IReadOnlyList<ProbeMetricCandidate> GetCandidates(
        ProbeCalibrationSnapshot snapshot,
        ProbeDimensionDefinition definition)
    {
        if (!snapshot.Blocks.TryGetValue("baseline", out ProbeMetricBlock? baseline) ||
            !snapshot.Blocks.TryGetValue(definition.CalibrationBlockId, out ProbeMetricBlock? condition))
            return Array.Empty<ProbeMetricCandidate>();

        var candidates = new List<ProbeMetricCandidate>();
        foreach ((string metricId, ProbeMetricValue baselineValue) in baseline.Metrics)
        {
            if (!baselineValue.Valid || !condition.Metrics.TryGetValue(metricId, out ProbeMetricValue? conditionValue) ||
                !conditionValue.Valid || double.IsNaN(baselineValue.Value) || double.IsNaN(conditionValue.Value))
                continue;

            candidates.Add(new ProbeMetricCandidate
            {
                MetricId = metricId,
                MetricName = baselineValue.MetricName,
                Unit = baselineValue.Unit,
                BaselineValue = baselineValue.Value,
                CalibrationValue = conditionValue.Value,
                Delta = conditionValue.Value - baselineValue.Value,
                SourceParticipantCount = 1,
                DirectionAgreement = 1d,
                EvidenceScope = "participant calibration"
            });
        }

        return candidates
            .OrderByDescending(candidate => Math.Abs(candidate.Delta))
            .ThenBy(candidate => candidate.MetricName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Loads one latest complete calibration run per participant. This is the source for
    /// a study-level direction: condition minus baseline is calculated within each person
    /// before the Console summarises its median direction and agreement rate.
    /// </summary>
    public ProbeCalibrationCohort LoadCohort(string outputRoot)
    {
        if (!Directory.Exists(outputRoot))
            return new ProbeCalibrationCohort();

        var snapshots = new List<ProbeCalibrationSnapshot>();
        foreach (string participantDirectory in Directory.EnumerateDirectories(outputRoot)
                     .Where(path => !Path.GetFileName(path).Equals("ResearcherConsole", StringComparison.OrdinalIgnoreCase)))
        {
            string participantId = Path.GetFileName(participantDirectory);
            IEnumerable<string> workloadDirectories = Directory
                .EnumerateFiles(participantDirectory, "XRWorkloadProbe_Behavior_*_Metrics.csv", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.Directory?.Parent != null)
                .GroupBy(file => file.Directory!.Parent!.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key)
                .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path));

            foreach (string workloadDirectory in workloadDirectories)
            {
                Dictionary<string, ProbeMetricBlock> blocks = ReadMetricBlocks(workloadDirectory, participantId);
                if (!blocks.ContainsKey("baseline") ||
                    (!blocks.ContainsKey(ProbeDimensionCatalog.Mental.CalibrationBlockId) &&
                     !blocks.ContainsKey(ProbeDimensionCatalog.Physical.CalibrationBlockId)))
                    continue;

                snapshots.Add(new ProbeCalibrationSnapshot
                {
                    ParticipantId = participantId,
                    RunDirectory = workloadDirectory,
                    Blocks = blocks
                });
                break;
            }
        }

        return new ProbeCalibrationCohort { Participants = snapshots };
    }

    public ProbeCalibrationSource CreateSource(ProbeCalibrationSnapshot snapshot)
    {
        snapshot.Blocks.TryGetValue("baseline", out ProbeMetricBlock? baseline);
        snapshot.Blocks.TryGetValue(ProbeDimensionCatalog.Mental.CalibrationBlockId, out ProbeMetricBlock? mental);
        snapshot.Blocks.TryGetValue(ProbeDimensionCatalog.Physical.CalibrationBlockId, out ProbeMetricBlock? physical);
        return new ProbeCalibrationSource
        {
            ParticipantId = snapshot.ParticipantId,
            RunDirectory = snapshot.RunDirectory,
            BaselineMetricsPath = baseline?.FilePath ?? "",
            MentalMetricsPath = mental?.FilePath ?? "",
            PhysicalMetricsPath = physical?.FilePath ?? ""
        };
    }

    public IReadOnlyList<ProbeMetricCandidate> GetCohortCandidates(
        ProbeCalibrationCohort cohort,
        ProbeDimensionDefinition definition)
    {
        var grouped = new Dictionary<string, List<(ProbeMetricValue Baseline, ProbeMetricValue Condition)>>(StringComparer.OrdinalIgnoreCase);
        foreach (ProbeCalibrationSnapshot snapshot in cohort.Participants)
        {
            if (!snapshot.Blocks.TryGetValue("baseline", out ProbeMetricBlock? baseline) ||
                !snapshot.Blocks.TryGetValue(definition.CalibrationBlockId, out ProbeMetricBlock? condition))
                continue;

            foreach ((string metricId, ProbeMetricValue baselineValue) in baseline.Metrics)
            {
                if (!baselineValue.Valid || !condition.Metrics.TryGetValue(metricId, out ProbeMetricValue? conditionValue) ||
                    !conditionValue.Valid)
                    continue;
                if (!grouped.TryGetValue(metricId, out List<(ProbeMetricValue, ProbeMetricValue)>? values))
                {
                    values = new List<(ProbeMetricValue, ProbeMetricValue)>();
                    grouped[metricId] = values;
                }
                values.Add((baselineValue, conditionValue));
            }
        }

        var candidates = new List<ProbeMetricCandidate>();
        foreach ((string metricId, List<(ProbeMetricValue Baseline, ProbeMetricValue Condition)> values) in grouped)
        {
            if (values.Count == 0)
                continue;
            var deltas = values.Select(value => value.Condition.Value - value.Baseline.Value).ToList();
            int positive = deltas.Count(delta => delta > 0d);
            int negative = deltas.Count(delta => delta < 0d);
            int directional = positive + negative;
            double directionAgreement = directional == 0
                ? 0d
                : Math.Max(positive, negative) / (double)directional;
            double medianDelta = Median(deltas);
            (ProbeMetricValue baselineSample, ProbeMetricValue conditionSample) = values[0];
            candidates.Add(new ProbeMetricCandidate
            {
                MetricId = metricId,
                MetricName = baselineSample.MetricName,
                Unit = baselineSample.Unit,
                BaselineValue = Median(values.Select(value => value.Baseline.Value)),
                CalibrationValue = Median(values.Select(value => value.Condition.Value)),
                Delta = medianDelta,
                SourceParticipantCount = values.Count,
                DirectionAgreement = directionAgreement,
                EvidenceScope = "calibration cohort"
            });
        }

        return candidates
            .OrderByDescending(candidate => candidate.DirectionAgreement)
            .ThenByDescending(candidate => Math.Abs(candidate.Delta))
            .ThenBy(candidate => candidate.MetricName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Dictionary<string, ProbeMetricBlock> ReadMetricBlocks(string runDirectory, string participantId)
    {
        var blocks = new Dictionary<string, ProbeMetricBlock>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(runDirectory))
            return blocks;

        foreach (FileInfo file in Directory.EnumerateFiles(runDirectory, "*Metrics.csv", SearchOption.AllDirectories)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(item => item.LastWriteTimeUtc))
        {
            ProbeMetricBlock? block = ReadMetricBlock(file.FullName, participantId);
            if (block == null || string.IsNullOrWhiteSpace(block.BlockId) || blocks.ContainsKey(block.BlockId))
                continue;
            blocks[block.BlockId] = block;
        }
        return blocks;
    }

    private static ProbeMetricBlock? ReadMetricBlock(string path, string participantId)
    {
        try
        {
            CsvTable table = CsvTable.Read(path);
            CsvRecord? first = table.Records.FirstOrDefault();
            if (first == null)
                return null;
            if (!string.IsNullOrWhiteSpace(participantId) &&
                !first.Get("participantId").Equals(participantId, StringComparison.OrdinalIgnoreCase))
                return null;

            string blockId = first.Get("blockId");
            var metrics = new Dictionary<string, ProbeMetricValue>(StringComparer.OrdinalIgnoreCase);
            foreach (CsvRecord row in table.Records)
            {
                string metricId = row.Get("metricId");
                if (string.IsNullOrWhiteSpace(metricId) || metrics.ContainsKey(metricId))
                    continue;
                double value = row.GetDouble("value");
                if (double.IsNaN(value))
                    continue;
                metrics[metricId] = new ProbeMetricValue
                {
                    MetricId = metricId,
                    MetricName = row.Get("metricName"),
                    Unit = row.Get("unit"),
                    Value = value,
                    Valid = row.GetBool("valid")
                };
            }

            return new ProbeMetricBlock
            {
                BlockId = blockId,
                TargetDimension = first.Get("targetDimension"),
                FilePath = path,
                Metrics = metrics
            };
        }
        catch
        {
            return null;
        }
    }

    private static double Median(IEnumerable<double> source)
    {
        List<double> values = source.Where(value => !double.IsNaN(value) && !double.IsInfinity(value)).OrderBy(value => value).ToList();
        if (values.Count == 0)
            return double.NaN;
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2d
            : values[middle];
    }
}

internal static class ProbeRuleCardStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string PluginDirectory(string outputRoot) => Path.Combine(
        outputRoot, "ResearcherConsole", "ProbePlugins");

    public static string ActiveSelectionPath(string outputRoot) => Path.Combine(
        PluginDirectory(outputRoot), "active_probe_plugin.json");

    // Retained as the active-plugin location for existing callers.
    public static string GetPath(string outputRoot) => ActiveSelectionPath(outputRoot);

    public static ProbeRuleCardSet Load(string outputRoot)
    {
        return LoadActive(outputRoot).Plugin;
    }

    public static ProbePluginFile LoadActive(string outputRoot)
    {
        string directory = PluginDirectory(outputRoot);
        string selectionPath = ActiveSelectionPath(outputRoot);
        string? activeFileName = null;
        try
        {
            if (File.Exists(selectionPath))
                activeFileName = JsonSerializer.Deserialize<ActiveProbePluginSelection>(
                    File.ReadAllText(selectionPath), JsonOptions)?.PluginFileName;
        }
        catch
        {
            activeFileName = null;
        }

        IEnumerable<string> candidates = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).Equals("active_probe_plugin.json", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();
        string? path = !string.IsNullOrWhiteSpace(activeFileName)
            ? candidates.FirstOrDefault(candidate => Path.GetFileName(candidate).Equals(activeFileName, StringComparison.OrdinalIgnoreCase))
            : candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (path == null)
            return new ProbePluginFile();
        try
        {
            ProbeRuleCardSet plugin = JsonSerializer.Deserialize<ProbeRuleCardSet>(File.ReadAllText(path), JsonOptions)
                                      ?? new ProbeRuleCardSet();
            return new ProbePluginFile { FilePath = path, Plugin = plugin, IsActive = true };
        }
        catch
        {
            return new ProbePluginFile();
        }
    }

    public static IReadOnlyList<ProbePluginFile> ListPlugins(string outputRoot)
    {
        ProbePluginFile active = LoadActive(outputRoot);
        string directory = PluginDirectory(outputRoot);
        if (!Directory.Exists(directory))
            return Array.Empty<ProbePluginFile>();
        var result = new List<ProbePluginFile>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => !Path.GetFileName(path).Equals("active_probe_plugin.json", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                ProbeRuleCardSet plugin = JsonSerializer.Deserialize<ProbeRuleCardSet>(File.ReadAllText(path), JsonOptions)
                                          ?? new ProbeRuleCardSet();
                result.Add(new ProbePluginFile
                {
                    FilePath = path,
                    Plugin = plugin,
                    IsActive = path.Equals(active.FilePath, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch
            {
                // Keep malformed plugin files out of the selectable library.
            }
        }
        return result;
    }

    public static string Save(string outputRoot, ProbeRuleCardSet set)
    {
        string directory = PluginDirectory(outputRoot);
        Directory.CreateDirectory(directory);
        set.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(set.PluginId))
            set.PluginId = Guid.NewGuid().ToString("N");
        string fileName = $"{SanitizeFileName(set.PluginName)}_{set.PluginId[..Math.Min(8, set.PluginId.Length)]}.json";
        string path = Path.Combine(directory, fileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(set, JsonOptions));
        File.Move(temporary, path, true);
        SetActive(outputRoot, path);
        return path;
    }

    public static void SetActive(string outputRoot, string pluginPath)
    {
        string directory = PluginDirectory(outputRoot);
        Directory.CreateDirectory(directory);
        string selectionPath = ActiveSelectionPath(outputRoot);
        string temporary = selectionPath + ".tmp";
        var selection = new ActiveProbePluginSelection
        {
            PluginFileName = Path.GetFileName(pluginPath),
            SelectedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        File.WriteAllText(temporary, JsonSerializer.Serialize(selection, JsonOptions));
        File.Move(temporary, selectionPath, true);
    }

    private static string SanitizeFileName(string value)
    {
        string source = string.IsNullOrWhiteSpace(value) ? "TaskProbe" : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var characters = source.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        string compact = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(compact) ? "TaskProbe" : compact;
    }
}
