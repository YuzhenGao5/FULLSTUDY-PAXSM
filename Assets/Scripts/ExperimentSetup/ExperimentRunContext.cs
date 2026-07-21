using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class ExperimentRunContext
{
    public const string StandardOutputFolderName = "CARE-XR Data";
    public const string StandaloneFolderName = "Standalone";

    [Serializable]
    public sealed class RunManifest
    {
        public string schemaVersion = "CAREXR_RunManifest_v1.0";
        public string participantId = "";
        public int sessionNumber;
        public string runId = "";
        public string selectedSceneId = "";
        public string selectedSceneName = "";
        public string dataNamespace = "";
        public string outputRoot = "";
        public string runDirectory = "";
        public string configuredAtUtc = "";
        public string unityVersion = "";
        public string applicationVersion = "";
        public string platform = "";
        public string deviceModel = "";
        public string operatingSystem = "";
    }

    [Serializable]
    public sealed class LastSetup
    {
        public string selectedSceneId = "";
        public string outputRoot = "";
    }

    static RunManifest _current;

    public static bool IsConfigured => _current != null;
    public static RunManifest Current => _current;
    public static string ParticipantId => _current != null ? _current.participantId : "";
    public static int SessionNumber => _current != null ? _current.sessionNumber : 0;
    public static string RunId => _current != null ? _current.runId : "";
    public static string RunDirectory => _current != null ? _current.runDirectory : "";

    public static void ClearActiveRun()
    {
        _current = null;
    }

    public static string GetDefaultOutputRoot()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Path.Combine(Application.persistentDataPath, "CAREXR_Experiment_Data");
#else
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
            return Path.Combine(desktop, StandardOutputFolderName);
        return Path.Combine(Application.persistentDataPath, "CAREXR_Experiment_Data");
#endif
    }

    public static string GetStandaloneOutputRoot()
    {
        string root = Path.Combine(GetDefaultOutputRoot(), StandaloneFolderName);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string MigrateSavedOutputRoot(string savedOutputRoot)
    {
        if (string.IsNullOrWhiteSpace(savedOutputRoot))
            return GetDefaultOutputRoot();

#if !UNITY_ANDROID || UNITY_EDITOR
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            string legacyDefault = Path.Combine(documents, StandardOutputFolderName);
            try
            {
                if (string.Equals(
                    Path.GetFullPath(savedOutputRoot.Trim()),
                    Path.GetFullPath(legacyDefault),
                    StringComparison.OrdinalIgnoreCase))
                    return GetDefaultOutputRoot();
            }
            catch
            {
                // Keep a custom saved path; validation will report malformed paths later.
            }
        }
#endif

        return savedOutputRoot.Trim();
    }

    public static bool ValidateParticipantId(string value, out string normalized, out string error)
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

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                continue;
            error = "Participant ID may contain letters, numbers, hyphens, and underscores only.";
            return false;
        }

        return true;
    }

    public static bool ValidateOutputRoot(string value, out string normalized, out string error)
    {
        error = "";
        string candidate = string.IsNullOrWhiteSpace(value) ? GetDefaultOutputRoot() : value.Trim();
        try
        {
            normalized = Path.GetFullPath(candidate);
            Directory.CreateDirectory(normalized);

            string probePath = Path.Combine(normalized, $".carexr_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "write-test", Encoding.UTF8);
            File.Delete(probePath);
            return true;
        }
        catch (Exception exception)
        {
            normalized = candidate;
            error = $"Output location is not writable: {exception.Message}";
            return false;
        }
    }

    public static bool Configure(
        ExperimentSceneCatalog.SceneEntry scene,
        string participantId,
        string outputRoot,
        out string error)
    {
        return Configure(scene, participantId, outputRoot, 0, out error);
    }

    public static bool Configure(
        ExperimentSceneCatalog.SceneEntry scene,
        string participantId,
        string outputRoot,
        int sessionNumber,
        out string error)
    {
        error = "";
        if (scene == null || string.IsNullOrWhiteSpace(scene.sceneName))
        {
            error = "Select an experiment scene.";
            return false;
        }

        if (!ValidateParticipantId(participantId, out string normalizedParticipantId, out error))
            return false;
        if (!ValidateOutputRoot(outputRoot, out string normalizedOutputRoot, out error))
            return false;

        string runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "_" +
                       Guid.NewGuid().ToString("N").Substring(0, 6);
        string sceneSegment = SafePathSegment(
            string.IsNullOrWhiteSpace(scene.id) ? scene.sceneName : scene.id,
            "scene");
        string runDirectory = Path.Combine(
            normalizedOutputRoot,
            SafePathSegment(normalizedParticipantId, "participant"),
            runId,
            sceneSegment);

        try
        {
            Directory.CreateDirectory(runDirectory);
            var manifest = new RunManifest
            {
                participantId = normalizedParticipantId,
                sessionNumber = Math.Max(0, sessionNumber),
                runId = runId,
                selectedSceneId = scene.id ?? "",
                selectedSceneName = scene.sceneName,
                dataNamespace = scene.dataNamespace ?? "",
                outputRoot = normalizedOutputRoot,
                runDirectory = runDirectory,
                configuredAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                applicationVersion = Application.version,
                platform = Application.platform.ToString(),
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem
            };

            string manifestPath = Path.Combine(runDirectory, "experiment_run_manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
            _current = manifest;
            SaveLastSetup(scene.id, normalizedOutputRoot);
            return true;
        }
        catch (Exception exception)
        {
            _current = null;
            error = $"Could not create the experiment run folder: {exception.Message}";
            return false;
        }
    }

    public static string ResolveRunDirectory(string fallbackRoot = null)
    {
        if (IsConfigured)
        {
            Directory.CreateDirectory(_current.runDirectory);
            return _current.runDirectory;
        }

        string root = string.IsNullOrWhiteSpace(fallbackRoot)
            ? GetStandaloneOutputRoot()
            : fallbackRoot;
        Directory.CreateDirectory(root);
        return root;
    }

    public static string ResolveOutputDirectory(string fallbackSubfolder)
    {
        if (!IsConfigured)
        {
            string fallback = Path.Combine(
                GetStandaloneOutputRoot(),
                SafePathSegment(fallbackSubfolder, "Experiment_Data"));
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        string dataNamespace = string.IsNullOrWhiteSpace(fallbackSubfolder)
            ? _current.dataNamespace
            : fallbackSubfolder;
        string directory = Path.Combine(
            _current.runDirectory,
            SafePathSegment(dataNamespace, "Experiment_Data"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string ParticipantIdOr(string fallback)
    {
        return IsConfigured && !string.IsNullOrWhiteSpace(_current.participantId)
            ? _current.participantId
            : fallback;
    }

    public static bool TryGetParticipantNumber(out int participantNumber)
    {
        participantNumber = 0;
        if (!IsConfigured || string.IsNullOrWhiteSpace(_current.participantId))
            return false;

        var digits = new StringBuilder();
        for (int i = 0; i < _current.participantId.Length; i++)
        {
            char c = _current.participantId[i];
            if (char.IsDigit(c))
                digits.Append(c);
        }

        return int.TryParse(digits.ToString(), out participantNumber) && participantNumber > 0;
    }

    public static bool TryLoadLastSetup(out LastSetup setup)
    {
        setup = null;
        try
        {
            string path = LastSetupPath();
            if (!File.Exists(path))
                return false;
            setup = JsonUtility.FromJson<LastSetup>(File.ReadAllText(path, Encoding.UTF8));
            return setup != null;
        }
        catch
        {
            setup = null;
            return false;
        }
    }

    public static string BuildPreviewPath(
        string outputRoot,
        string participantId,
        ExperimentSceneCatalog.SceneEntry scene)
    {
        string root = string.IsNullOrWhiteSpace(outputRoot) ? GetDefaultOutputRoot() : outputRoot.Trim();
        string participant = string.IsNullOrWhiteSpace(participantId) ? "{participant-id}" : participantId.Trim();
        string sceneId = scene == null
            ? "{scene}"
            : SafePathSegment(string.IsNullOrWhiteSpace(scene.id) ? scene.sceneName : scene.id, "scene");
        return Path.Combine(root, participant, "{run-id}", sceneId);
    }

    static void SaveLastSetup(string selectedSceneId, string outputRoot)
    {
        try
        {
            string path = LastSetupPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var setup = new LastSetup
            {
                selectedSceneId = selectedSceneId ?? "",
                outputRoot = outputRoot ?? ""
            };
            File.WriteAllText(path, JsonUtility.ToJson(setup, true), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ExperimentSetup] Could not save the last setup: {exception.Message}");
        }
    }

    static string LastSetupPath()
    {
        return Path.Combine(Application.persistentDataPath, "ExperimentSetup", "last_setup.json");
    }

    static string SafePathSegment(string value, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            bool isInvalid = false;
            for (int j = 0; j < invalid.Length; j++)
            {
                if (c != invalid[j])
                    continue;
                isInvalid = true;
                break;
            }
            builder.Append(isInvalid ? '_' : c);
        }

        string result = builder.ToString().Trim();
        return result.Length > 0 ? result : fallback;
    }
}
