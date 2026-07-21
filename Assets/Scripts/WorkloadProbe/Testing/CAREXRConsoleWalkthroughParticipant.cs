#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only participant driver for researcher-console walkthroughs.
/// The console owns participant context and scene switching; this component only
/// supplies deterministic participant input and waits for the real scene export.
/// </summary>
public sealed class CAREXRConsoleWalkthroughParticipant : MonoBehaviour
{
    [Serializable]
    public sealed class Request
    {
        public string schemaVersion = "CAREXR_ConsoleWalkthroughRequest_v1";
        public string participantId = "ID7878";
        public string expectedSceneName = "";
        public string requestedAtUtc = "";
    }

    [Serializable]
    sealed class Summary
    {
        public string schemaVersion = "CAREXR_ConsoleWalkthroughResult_v1";
        public string participantId = "";
        public int sessionNumber;
        public string sceneName = "";
        public string runDirectory = "";
        public string startedUtc = "";
        public string completedUtc = "";
        public bool participantContextMatched;
        public bool exportCompleted;
        public bool integrityRequired;
        public bool integrityPassed;
        public string integrityManifest = "";
        public string error = "";
    }

    static CAREXRConsoleWalkthroughParticipant _instance;
    Request _request;
    Summary _summary;

    public static void Begin(Request request)
    {
        if (_instance != null || request == null)
            return;

        CAREXRSyntheticParticipantRuntime.Activate(request.participantId);
        var host = new GameObject("CARE-XR Console Walkthrough Participant");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<CAREXRConsoleWalkthroughParticipant>();
        _instance._request = request;
        _instance.StartCoroutine(_instance.Run());
    }

    IEnumerator Run()
    {
        _summary = new Summary
        {
            participantId = _request.participantId,
            sceneName = _request.expectedSceneName,
            startedUtc = DateTime.UtcNow.ToString("O"),
            integrityRequired = IsProbeScene(_request.expectedSceneName)
        };

        float sceneDeadline = Time.realtimeSinceStartup + 45f;
        while (!string.Equals(
                   SceneManager.GetActiveScene().name,
                   _request.expectedSceneName,
                   StringComparison.Ordinal) &&
               Time.realtimeSinceStartup < sceneDeadline)
            yield return null;

        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                _request.expectedSceneName,
                StringComparison.Ordinal))
        {
            _summary.error = $"Terminal did not load {_request.expectedSceneName} within 45 seconds.";
            yield return Finish();
            yield break;
        }

        _summary.sessionNumber = ExperimentRunContext.SessionNumber;
        _summary.runDirectory = ExperimentRunContext.RunDirectory;
        _summary.participantContextMatched = ExperimentRunContext.IsConfigured &&
                                             string.Equals(
                                                 ExperimentRunContext.ParticipantId,
                                                 _request.participantId,
                                                 StringComparison.OrdinalIgnoreCase);
        if (!_summary.participantContextMatched)
        {
            _summary.error =
                $"Participant context mismatch. Expected {_request.participantId}, " +
                $"observed {ExperimentRunContext.ParticipantIdOr("<none>")}.";
            yield return Finish();
            yield break;
        }

        WriteSyntheticMarker();

        XRWorkloadProbeSceneController controller = null;
        float controllerDeadline = Time.realtimeSinceStartup + 20f;
        while (controller == null && Time.realtimeSinceStartup < controllerDeadline)
        {
            controller = FindFirstObjectByType<XRWorkloadProbeSceneController>();
            yield return null;
        }
        if (controller == null)
        {
            _summary.error = $"{_request.expectedSceneName} did not create its experiment controller.";
            yield return Finish();
            yield break;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo completedField = typeof(XRWorkloadProbeSceneController).GetField(
            "_completedExportWritten",
            flags);
        FieldInfo integrityPassedField = typeof(XRWorkloadProbeSceneController).GetField(
            "_lastDataIntegrityPassed",
            flags);
        FieldInfo integrityPathField = typeof(XRWorkloadProbeSceneController).GetField(
            "_lastDataIntegrityPath",
            flags);

        float completionDeadline = Time.realtimeSinceStartup + 900f;
        while (Time.realtimeSinceStartup < completionDeadline)
        {
            _summary.exportCompleted = completedField != null &&
                                       completedField.GetValue(controller) is bool completed &&
                                       completed;
            if (_summary.exportCompleted)
                break;
            yield return null;
        }

        if (!_summary.exportCompleted)
        {
            _summary.error = $"{_request.expectedSceneName} did not export within 15 minutes.";
        }
        else if (_summary.integrityRequired)
        {
            _summary.integrityPassed = integrityPassedField != null &&
                                       integrityPassedField.GetValue(controller) is bool passed &&
                                       passed;
            _summary.integrityManifest = integrityPathField?.GetValue(controller) as string ?? "";
            if (!_summary.integrityPassed)
                _summary.error = "Scene export completed, but its data-integrity check failed.";
        }

        yield return Finish();
    }

    IEnumerator Finish()
    {
        _summary.completedUtc = DateTime.UtcNow.ToString("O");
        WriteSummary();
        CAREXRSyntheticParticipantRuntime.Deactivate();
        Debug.Log(string.IsNullOrEmpty(_summary.error)
            ? $"[CARE-XR Console Walkthrough] {_summary.participantId} completed {_summary.sceneName}."
            : $"[CARE-XR Console Walkthrough] {_summary.sceneName} failed: {_summary.error}");
        yield return new WaitForSecondsRealtime(0.75f);
        UnityEditor.EditorApplication.isPlaying = false;
    }

    void WriteSyntheticMarker()
    {
        if (string.IsNullOrWhiteSpace(_summary.runDirectory))
            return;
        Directory.CreateDirectory(_summary.runDirectory);
        string marker =
            "SYNTHETIC TEST DATA - DO NOT INCLUDE IN HUMAN-PARTICIPANT ANALYSIS\n" +
            $"participantId={_summary.participantId}\n" +
            "scenario=researcher_console_walkthrough_v1\n" +
            $"scene={_summary.sceneName}\n" +
            $"createdUtc={DateTime.UtcNow:O}\n";
        File.WriteAllText(
            Path.Combine(_summary.runDirectory, "SYNTHETIC_TEST_RUN.txt"),
            marker,
            Encoding.UTF8);
    }

    void WriteSummary()
    {
        try
        {
            string root = string.IsNullOrWhiteSpace(_summary.runDirectory)
                ? Path.Combine(ExperimentRunContext.GetDefaultOutputRoot(), _summary.participantId)
                : _summary.runDirectory;
            Directory.CreateDirectory(root);
            string safeScene = string.IsNullOrWhiteSpace(_summary.sceneName)
                ? "UnknownScene"
                : _summary.sceneName;
            string path = Path.Combine(
                root,
                $"CONSOLE_WALKTHROUGH_{_summary.participantId}_{safeScene}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonUtility.ToJson(_summary, true), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[CARE-XR Console Walkthrough] Could not write summary: {exception}");
        }
    }

    static bool IsProbeScene(string sceneName)
    {
        return string.Equals(sceneName, "XRWorkloadProbeScene", StringComparison.Ordinal) ||
               string.Equals(sceneName, "XRCombinedProbeScene", StringComparison.Ordinal);
    }
}
#endif
