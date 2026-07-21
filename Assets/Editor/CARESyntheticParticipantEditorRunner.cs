using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CAREXRSyntheticParticipantEditorRunner
{
    [Serializable]
    sealed class Request
    {
        public string participantId = "P100";
        public string outputRoot = "";
        public bool runWorkloadProbe = true;
        public bool useExperimentSetup;
        public bool runCombinedProbeOnly;
    }

    const string PendingSessionKey = "CAREXR.SyntheticParticipant.PendingRequest";
    static double _nextRequestCheck;

    static CAREXRSyntheticParticipantEditorRunner()
    {
        EditorApplication.update += CheckForRequest;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // A script reload can interrupt an active synthetic run. If a new request is
        // waiting, return to Edit Mode first so the request can be started cleanly.
        if (EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(RequestPath()))
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
    }

    [MenuItem("CARE-XR/Testing/Run P100 Comparison + Workload Probe (Synthetic)")]
    public static void QueueP100Run()
    {
        string requestPath = RequestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath));
        var request = new Request
        {
            participantId = "P100",
            outputRoot = ExperimentRunContext.GetDefaultOutputRoot()
        };
        File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));
        AssetDatabase.Refresh();
        Debug.Log($"[CARE-XR Synthetic] Queued P100 run via {requestPath}");
    }

    [MenuItem("CARE-XR/Testing/Run P888 Comparison via Experiment Setup (Synthetic)")]
    public static void QueueP888SetupRun()
    {
        string requestPath = RequestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath));
        var request = new Request
        {
            participantId = "P888",
            outputRoot = ExperimentRunContext.GetDefaultOutputRoot(),
            runWorkloadProbe = false,
            useExperimentSetup = true
        };
        File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));
        AssetDatabase.Refresh();
        Debug.Log($"[CARE-XR Synthetic] Queued P888 comparison run through Experiment Setup via {requestPath}");
    }

    [MenuItem("CARE-XR/Testing/Run P888 Combined Probe Only (Synthetic)")]
    public static void QueueP888CombinedProbeRun()
    {
        string requestPath = RequestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(requestPath));
        var request = new Request
        {
            participantId = "P888",
            outputRoot = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? ".",
                "Temp",
                "CombinedProbeSyntheticOutput"),
            runWorkloadProbe = true,
            runCombinedProbeOnly = true
        };
        File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));
        AssetDatabase.Refresh();
        Debug.Log($"[CARE-XR Synthetic] Queued P888 combined-probe run via {requestPath}");
    }

    static void CheckForRequest()
    {
        if (EditorApplication.timeSinceStartup < _nextRequestCheck)
            return;
        _nextRequestCheck = EditorApplication.timeSinceStartup + 0.5d;

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string path = RequestPath();
        if (!File.Exists(path))
            return;

        string json = File.ReadAllText(path);
        Request request = JsonUtility.FromJson<Request>(json) ?? new Request();
        if (string.IsNullOrWhiteSpace(request.outputRoot))
            request.outputRoot = ExperimentRunContext.GetDefaultOutputRoot();

        if (request.useExperimentSetup &&
            !string.Equals(SceneManager.GetActiveScene().name, "ExperimentSetup", StringComparison.Ordinal))
        {
            Debug.LogError(
                "[CARE-XR Synthetic] The ExperimentSetup scene must be open before running the setup-path test.");
            File.Delete(path);
            return;
        }

        SessionState.SetString(PendingSessionKey, JsonUtility.ToJson(request));
        File.Delete(path);
        EditorApplication.isPlaying = true;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
            return;

        string json = SessionState.GetString(PendingSessionKey, "");
        if (string.IsNullOrWhiteSpace(json))
            return;

        SessionState.EraseString(PendingSessionKey);
        Request request = JsonUtility.FromJson<Request>(json) ?? new Request();
        if (request.runCombinedProbeOnly)
        {
            CAREXRSyntheticParticipantOrchestrator.BeginCombinedProbeOnly(
                request.participantId,
                request.outputRoot);
        }
        else if (request.useExperimentSetup)
        {
            CAREXRSyntheticParticipantOrchestrator.BeginFromExperimentSetup(
                request.participantId,
                request.outputRoot,
                request.runWorkloadProbe);
        }
        else
        {
            CAREXRSyntheticParticipantOrchestrator.Begin(
                request.participantId,
                request.outputRoot,
                request.runWorkloadProbe);
        }
    }

    static string RequestPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
        return Path.Combine(projectRoot, "Temp", "CAREXR_SyntheticParticipant.request.json");
    }
}
