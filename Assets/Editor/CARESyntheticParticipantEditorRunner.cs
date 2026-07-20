using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CAREXRSyntheticParticipantEditorRunner
{
    [Serializable]
    sealed class Request
    {
        public string participantId = "P100";
        public string outputRoot = "";
        public bool runWorkloadProbe = true;
    }

    const string PendingSessionKey = "CAREXR.SyntheticParticipant.PendingRequest";
    static double _nextRequestCheck;

    static CAREXRSyntheticParticipantEditorRunner()
    {
        EditorApplication.update += CheckForRequest;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
        CAREXRSyntheticParticipantOrchestrator.Begin(
            request.participantId,
            request.outputRoot,
            request.runWorkloadProbe);
    }

    static string RequestPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
        return Path.Combine(projectRoot, "Temp", "CAREXR_SyntheticParticipant.request.json");
    }
}
