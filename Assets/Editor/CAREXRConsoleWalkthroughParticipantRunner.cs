using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CAREXRConsoleWalkthroughParticipantRunner
{
    public const string SchemaVersion = "CAREXR_ConsoleWalkthroughRequest_v1";

    static CAREXRConsoleWalkthroughParticipantRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
            return;

        string path = RequestPath();
        if (!File.Exists(path))
            return;

        CAREXRConsoleWalkthroughParticipant.Request request;
        try
        {
            request = JsonUtility.FromJson<CAREXRConsoleWalkthroughParticipant.Request>(
                File.ReadAllText(path));
            File.Delete(path);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[CARE-XR Console Walkthrough] Invalid participant request: {exception.Message}");
            return;
        }

        if (request == null ||
            !string.Equals(request.schemaVersion, SchemaVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.participantId) ||
            string.IsNullOrWhiteSpace(request.expectedSceneName))
        {
            Debug.LogError("[CARE-XR Console Walkthrough] Participant request is incomplete.");
            return;
        }

        Debug.Log(
            $"[CARE-XR Console Walkthrough] Participant {request.participantId} armed for " +
            $"{request.expectedSceneName}; scene selection remains owned by Researcher Console.");
        CAREXRConsoleWalkthroughParticipant.Begin(request);
    }

    public static string RequestPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
        return Path.Combine(projectRoot, "Temp", "CAREXR_ConsoleWalkthroughParticipant.request.json");
    }
}
