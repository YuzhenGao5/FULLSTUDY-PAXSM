using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PAXSMResearcherConsole;

internal sealed class DataScanner
{
    private static readonly (string Id, string Name)[] ExpectedCalibrationBlocks =
    {
        ("baseline", "Baseline"),
        ("cognitive_heavy", "Cognitive-heavy"),
        ("physical_heavy", "Physical-heavy"),
        ("temporal_heavy", "Temporal-heavy")
    };

    public DataSnapshot Scan(ResearchSession session)
    {
        string participantDirectory = Path.Combine(session.OutputRoot, session.ParticipantId);
        var blocks = ExpectedCalibrationBlocks.Select(block => new CalibrationBlockSnapshot
        {
            BlockId = block.Id,
            DisplayName = block.Name,
            State = CalibrationBlockState.Queued
        }).ToList();

        if (!Directory.Exists(participantDirectory))
            return new DataSnapshot
            {
                ParticipantDirectory = participantDirectory,
                CalibrationBlocks = blocks
            };

        List<FileInfo> files;
        try
        {
            files = Directory.EnumerateFiles(participantDirectory, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .ToList();
        }
        catch
        {
            files = new List<FileInfo>();
        }

        List<RunManifestRecord> runs = ReadRunManifests(files, session);
        RunManifestRecord? workloadRun = runs
            .Where(run => string.Equals(run.SceneName, "XRWorkloadProbeScene", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.ConfiguredAtUtc)
            .FirstOrDefault();
        if (workloadRun != null)
            PopulateCalibrationBlocks(blocks, workloadRun.RunDirectory, session.ParticipantId);

        return new DataSnapshot
        {
            ParticipantDirectory = participantDirectory,
            CsvFileCount = files.Count(file => file.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)),
            JsonFileCount = files.Count(file => file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)),
            TotalBytes = files.Sum(file => file.Length),
            LatestWriteUtc = files.Count == 0
                ? null
                : new DateTimeOffset(files.Max(file => file.LastWriteTimeUtc), TimeSpan.Zero),
            Runs = runs,
            CalibrationBlocks = blocks
                .OrderBy(block => block.PresentationOrder ?? (block.BlockId == "baseline" ? 0 : 99))
                .ThenBy(block => Array.FindIndex(ExpectedCalibrationBlocks, item => item.Id == block.BlockId))
                .ToList(),
            LatestFiles = files
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(30)
                .ToList()
        };
    }

    private static List<RunManifestRecord> ReadRunManifests(
        IEnumerable<FileInfo> files,
        ResearchSession session)
    {
        var result = new List<RunManifestRecord>();
        foreach (FileInfo file in files.Where(file =>
                     file.Name.Equals("experiment_run_manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file.FullName));
                JsonElement root = document.RootElement;
                string participantId = GetString(root, "participantId");
                if (!string.Equals(participantId, session.ParticipantId, StringComparison.OrdinalIgnoreCase))
                    continue;

                int sessionNumber = GetInt(root, "sessionNumber");
                if (sessionNumber > 0 && sessionNumber != session.SessionNumber)
                    continue;

                DateTimeOffset.TryParse(
                    GetString(root, "configuredAtUtc"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset configuredAt);
                result.Add(new RunManifestRecord
                {
                    ParticipantId = participantId,
                    SessionNumber = sessionNumber,
                    RunId = GetString(root, "runId"),
                    SceneId = GetString(root, "selectedSceneId"),
                    SceneName = GetString(root, "selectedSceneName"),
                    RunDirectory = string.IsNullOrWhiteSpace(GetString(root, "runDirectory"))
                        ? file.DirectoryName ?? ""
                        : GetString(root, "runDirectory"),
                    ConfiguredAtUtc = configuredAt
                });
            }
            catch
            {
                // Keep scanning other runs; malformed files remain visible in the latest-files list.
            }
        }

        return result.OrderByDescending(run => run.ConfiguredAtUtc).ToList();
    }

    private static void PopulateCalibrationBlocks(
        List<CalibrationBlockSnapshot> blocks,
        string runDirectory,
        string participantId)
    {
        if (!Directory.Exists(runDirectory))
            return;

        FileInfo? runOrder = Directory.EnumerateFiles(
                runDirectory,
                $"WorkloadProbe_RunOrder_{participantId}_*.csv",
                SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (runOrder != null)
            ApplyRunOrder(blocks, runOrder.FullName);

        FileInfo? questionnaire = Directory.EnumerateFiles(
                runDirectory,
                $"CAREXR_Questionnaire_{participantId}_*.csv",
                SearchOption.AllDirectories)
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
            .FirstOrDefault();
        if (questionnaire != null)
            ApplyQuestionnaireProgress(blocks, questionnaire.FullName);
    }

    private static void ApplyRunOrder(List<CalibrationBlockSnapshot> blocks, string path)
    {
        List<string[]> rows = ReadCsv(path);
        if (rows.Count < 2)
            return;
        int blockIndex = ColumnIndex(rows[0], "blockId");
        int orderIndex = ColumnIndex(rows[0], "presentationOrder");
        if (blockIndex < 0 || orderIndex < 0)
            return;

        foreach (string[] row in rows.Skip(1))
        {
            if (row.Length <= Math.Max(blockIndex, orderIndex))
                continue;
            CalibrationBlockSnapshot? block = blocks.FirstOrDefault(item =>
                item.BlockId.Equals(row[blockIndex], StringComparison.OrdinalIgnoreCase));
            if (block != null && int.TryParse(row[orderIndex], out int order))
                block.PresentationOrder = order;
        }
    }

    private static void ApplyQuestionnaireProgress(List<CalibrationBlockSnapshot> blocks, string path)
    {
        List<string[]> rows = ReadCsv(path);
        if (rows.Count < 2)
            return;
        int blockIndex = ColumnIndex(rows[0], "blockId");
        int orderIndex = ColumnIndex(rows[0], "presentationOrder");
        int itemIndex = ColumnIndex(rows[0], "itemId");
        if (blockIndex < 0)
            return;

        foreach (IGrouping<string, string[]> group in rows.Skip(1)
                     .Where(row => row.Length > blockIndex)
                     .GroupBy(row => row[blockIndex], StringComparer.OrdinalIgnoreCase))
        {
            CalibrationBlockSnapshot? block = blocks.FirstOrDefault(item =>
                item.BlockId.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (block == null)
                continue;

            string[][] groupRows = group.ToArray();
            if (orderIndex >= 0)
            {
                string[] orderRow = groupRows.FirstOrDefault(row => row.Length > orderIndex) ?? Array.Empty<string>();
                if (orderRow.Length > orderIndex && int.TryParse(orderRow[orderIndex], out int order))
                    block.PresentationOrder = order;
            }

            block.QuestionnaireItemCount = itemIndex >= 0
                ? groupRows.Where(row => row.Length > itemIndex)
                    .Select(row => row[itemIndex])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()
                : groupRows.Length;
            block.State = block.QuestionnaireItemCount >= 6
                ? CalibrationBlockState.Collected
                : CalibrationBlockState.QuestionnairePartial;
        }
    }

    private static List<string[]> ReadCsv(string path)
    {
        var rows = new List<string[]>();
        try
        {
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
                rows.Add(ParseCsvLine(line));
        }
        catch
        {
            // A Unity atomic replacement can briefly make a live checkpoint unavailable.
        }
        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }

    private static int ColumnIndex(string[] header, string name) =>
        Array.FindIndex(header, value => value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? ""
            : "";

    private static int GetInt(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement element) && element.TryGetInt32(out int value)
            ? value
            : 0;
}
