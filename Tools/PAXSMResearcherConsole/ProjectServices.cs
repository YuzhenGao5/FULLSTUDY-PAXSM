using System.Diagnostics;
using System.Text.Json;

namespace PAXSMResearcherConsole;

internal static class ProjectLocator
{
    public static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startPath));
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets")) &&
                Directory.Exists(Path.Combine(current.FullName, "ProjectSettings")))
                return current.FullName;
            current = current.Parent;
        }

        string workingDirectory = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(workingDirectory, "Assets")) &&
            Directory.Exists(Path.Combine(workingDirectory, "ProjectSettings")))
            return Path.GetFullPath(workingDirectory);

        return Path.GetFullPath(Path.Combine(startPath, "..", "..", "..", "..", ".."));
    }
}

internal sealed class ProjectServices
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProjectServices(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
    }

    public string ProjectRoot { get; }

    public string DefaultOutputRoot
    {
        get
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(
                string.IsNullOrWhiteSpace(desktop) ? ProjectRoot : desktop,
                "CARE-XR Data");
        }
    }

    public string LaunchRequestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PAXSM",
        "ResearcherConsole",
        "launch_request.json");

    public static bool TryNormalizeParticipantId(string? value, out string normalized, out string error)
    {
        normalized = (value ?? "").Trim();
        error = "";
        if (normalized.Length == 0)
        {
            error = "Participant ID is required.";
            return false;
        }

        if (normalized.Length > 48)
        {
            error = "Participant ID must be 48 characters or fewer.";
            return false;
        }

        if (normalized.Any(character =>
                !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
        {
            error = "Use letters, numbers, hyphens, and underscores only.";
            return false;
        }

        return true;
    }

    public bool TryCreateSession(
        string participantId,
        int sessionNumber,
        string outputRoot,
        out ResearchSession? session,
        out string error)
    {
        session = null;
        if (!TryNormalizeParticipantId(participantId, out string normalizedId, out error))
            return false;

        if (sessionNumber < 1 || sessionNumber > 999)
        {
            error = "Session number must be between 1 and 999.";
            return false;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(outputRoot) ? DefaultOutputRoot : outputRoot.Trim());
            Directory.CreateDirectory(normalizedRoot);
            string writeProbe = Path.Combine(normalizedRoot, $".paxsm_console_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(writeProbe, "write-test");
            File.Delete(writeProbe);
        }
        catch (Exception exception)
        {
            error = $"Data output location is not writable: {exception.Message}";
            return false;
        }

        session = new ResearchSession
        {
            ParticipantId = normalizedId,
            SessionNumber = sessionNumber,
            OutputRoot = normalizedRoot
        };

        try
        {
            WriteConsoleSessionManifest(session);
        }
        catch (Exception exception)
        {
            error = $"Could not create the console session manifest: {exception.Message}";
            session = null;
            return false;
        }

        error = "";
        return true;
    }

    public string ParticipantDirectory(ResearchSession session) =>
        Path.Combine(session.OutputRoot, session.ParticipantId);

    public string ConsoleSessionDirectory(ResearchSession session) => Path.Combine(
        session.OutputRoot,
        "ResearcherConsole",
        session.ParticipantId,
        $"Session_{session.SessionNumber}");

    public string WriteLaunchRequest(ResearchSession session, SceneDefinition scene)
    {
        var request = new ConsoleLaunchRequest
        {
            ProjectRoot = ProjectRoot,
            ParticipantId = session.ParticipantId,
            SessionNumber = session.SessionNumber,
            SceneId = scene.Id,
            SceneName = scene.SceneName,
            DataNamespace = scene.DataNamespace,
            OutputRoot = session.OutputRoot,
            AutoStart = true
        };

        string requestPath = LaunchRequestPath;
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
        WriteJsonAtomic(requestPath, request);
        WriteLaunchAudit(session, request);
        return requestPath;
    }

    public string QueueSceneLaunch(ResearchSession session, SceneDefinition scene)
    {
        WriteLaunchRequest(session, scene);

        if (TryGetRunningUnityEditor(out _))
            return $"Launch request queued for the open Unity Editor: {scene.SceneName}.";

        string? playerPath = FindExperimentPlayer();
        if (!string.IsNullOrWhiteSpace(playerPath))
        {
            Process.Start(new ProcessStartInfo(playerPath) { UseShellExecute = true });
            return $"Started experiment player: {Path.GetFileName(playerPath)}.";
        }

        string? unityEditor = FindUnityEditorPath();
        if (!string.IsNullOrWhiteSpace(unityEditor))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = unityEditor,
                Arguments = $"-projectPath \"{ProjectRoot}\"",
                UseShellExecute = true
            });
            return $"Opening the Unity project; {scene.SceneName} will start when the Editor bridge is ready.";
        }

        return "Launch request was written, but no Unity Editor or experiment player could be found.";
    }

    public void OpenParticipantFolder(ResearchSession session)
    {
        string path = ParticipantDirectory(session);
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenOutputRoot(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        Process.Start(new ProcessStartInfo(outputRoot) { UseShellExecute = true });
    }

    public bool TryOpenQuestionnaireAgent(out string message)
    {
        string batchPath = Path.Combine(ProjectRoot, "Tools", "PAXSMStudyAgent", "Open_PAXSMStudyAgent.bat");
        string exePath = Path.Combine(
            ProjectRoot,
            "Tools",
            "PAXSMStudyAgent",
            "bin",
            "Release",
            "net9.0-windows",
            "win-x64",
            "publish-v52",
            "PAXSMStudyAgent.exe");

        string? target = File.Exists(batchPath) ? batchPath : File.Exists(exePath) ? exePath : null;
        if (target == null)
        {
            message = "Questionnaire Agent executable was not found.";
            return false;
        }

        Process.Start(new ProcessStartInfo(target)
        {
            WorkingDirectory = Path.GetDirectoryName(target)!,
            UseShellExecute = true
        });
        message = "Questionnaire Agent opened as an independent configuration tool.";
        return true;
    }

    public bool TryGetRunningUnityEditor(out int processId)
    {
        processId = 0;
        string instancePath = Path.Combine(ProjectRoot, "Library", "EditorInstance.json");
        if (!File.Exists(instancePath))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(instancePath));
            if (!document.RootElement.TryGetProperty("process_id", out JsonElement processElement) ||
                !processElement.TryGetInt32(out processId))
                return false;
            Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            processId = 0;
            return false;
        }
    }

    private void WriteConsoleSessionManifest(ResearchSession session)
    {
        string directory = ConsoleSessionDirectory(session);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "console_session_manifest.json");
        string now = DateTimeOffset.UtcNow.ToString("O");
        var manifest = new ConsoleSessionManifest
        {
            ParticipantId = session.ParticipantId,
            SessionNumber = session.SessionNumber,
            OutputRoot = session.OutputRoot,
            ProjectRoot = ProjectRoot,
            OpenedAtUtc = now,
            LastUpdatedAtUtc = now
        };
        WriteJsonAtomic(path, manifest);
    }

    private void WriteLaunchAudit(ResearchSession session, ConsoleLaunchRequest request)
    {
        string directory = Path.Combine(ConsoleSessionDirectory(session), "launches");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{request.SceneId}_{request.RequestId[..6]}.json");
        WriteJsonAtomic(path, request);
    }

    private string? FindExperimentPlayer()
    {
        string builds = Path.Combine(ProjectRoot, "Builds");
        if (!Directory.Exists(builds))
            return null;

        return Directory.EnumerateFiles(builds, "*.exe", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Contains("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private string? FindUnityEditorPath()
    {
        string instancePath = Path.Combine(ProjectRoot, "Library", "EditorInstance.json");
        try
        {
            if (File.Exists(instancePath))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(instancePath));
                if (document.RootElement.TryGetProperty("app_path", out JsonElement appPathElement))
                {
                    string? path = appPathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        return path;
                }
            }
        }
        catch
        {
            // Fall through to the project-version lookup.
        }

        string versionFile = Path.Combine(ProjectRoot, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile))
            return null;

        string? versionLine = File.ReadLines(versionFile)
            .FirstOrDefault(line => line.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
        string? version = versionLine?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        if (string.IsNullOrWhiteSpace(version))
            return null;

        string candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity",
            "Hub",
            "Editor",
            version,
            "Editor",
            "Unity.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }
}
