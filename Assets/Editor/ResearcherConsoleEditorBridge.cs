using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ResearcherConsoleEditorBridge
{
    const string SetupScenePath = "Assets/Scenes/ExperimentSetup.unity";
    static double _nextPollTime;

    static ResearcherConsoleEditorBridge()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _nextPollTime)
            return;
        _nextPollTime = EditorApplication.timeSinceStartup + 0.75d;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (!ResearcherConsoleLaunchRequest.TryPeek(out ResearcherConsoleLaunchRequest.Payload request) ||
            !ResearcherConsoleLaunchRequest.MatchesCurrentProject(request))
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isDirty)
        {
            Debug.LogWarning(
                "[ResearcherConsole] Launch request is waiting because the current Unity scene has unsaved changes. " +
                "Save or discard them, then the request will continue.");
            return;
        }

        if (!File.Exists(Path.GetFullPath(SetupScenePath)))
        {
            Debug.LogError("[ResearcherConsole] ExperimentSetup scene is missing; launch request was not consumed.");
            return;
        }

        if (!string.Equals(activeScene.path, SetupScenePath, StringComparison.OrdinalIgnoreCase))
            EditorSceneManager.OpenScene(SetupScenePath, OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }
}
