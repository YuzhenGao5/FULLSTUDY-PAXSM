using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class XRCombinedProbeSceneBuilder
{
    const string SourceScenePath = "Assets/Scenes/XRWorkloadProbeScene.unity";
    public const string CombinedScenePath = "Assets/Scenes/XRCombinedProbeScene.unity";
    const string RebuildRequestFile = "CAREXR_CombinedProbeRebuild.request";
    static double _nextRequestCheck;

    [InitializeOnLoadMethod]
    static void RegisterRequestRunner()
    {
        EditorApplication.update -= CheckForRebuildRequest;
        EditorApplication.update += CheckForRebuildRequest;
    }

    static void CheckForRebuildRequest()
    {
        if (EditorApplication.timeSinceStartup < _nextRequestCheck)
            return;
        _nextRequestCheck = EditorApplication.timeSinceStartup + 0.5d;

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string requestPath = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? ".",
            "Temp",
            RebuildRequestFile);
        if (!File.Exists(requestPath))
            return;

        File.Delete(requestPath);
        Build();
    }

    [MenuItem("CARE-XR/Rebuild Combined Probe Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Build();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(CombinedScenePath);
    }

    public static void BuildFromCommandLine()
    {
        Build();
    }

    static void Build()
    {
        if (!File.Exists(Path.GetFullPath(SourceScenePath)))
            throw new FileNotFoundException("The workload-probe source scene is missing.", SourceScenePath);

        Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        XRWorkloadProbeSceneController source =
            UnityEngine.Object.FindFirstObjectByType<XRWorkloadProbeSceneController>();
        if (source == null)
            throw new InvalidOperationException("The workload-probe source scene has no controller.");

        GameObject host = source.gameObject;
        string serializedSource = EditorJsonUtility.ToJson(source);
        XRWorkloadProbeBehaviorCollector oldCollector =
            host.GetComponent<XRWorkloadProbeBehaviorCollector>();
        if (oldCollector != null)
            UnityEngine.Object.DestroyImmediate(oldCollector);
        UnityEngine.Object.DestroyImmediate(source);

        XRCombinedProbeSceneController controller =
            host.AddComponent<XRCombinedProbeSceneController>();
        EditorJsonUtility.FromJsonOverwrite(serializedSource, controller);
        controller.ConfigureCombinedExperiment();
        host.name = "XR Combined Probe Bootstrap";

        XRWorkloadProbeBehaviorCollector collector =
            host.AddComponent<XRWorkloadProbeBehaviorCollector>();
        collector.probeController = controller;
        collector.writeRawSamples = true;
        collector.sampleRateHz = 30f;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(collector);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, CombinedScenePath, true))
            throw new InvalidOperationException($"Could not save {CombinedScenePath}.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ExperimentSetupSceneBuilder.BuildFromCommandLine();
        ValidateFromCommandLine();
        Debug.Log("[XRCombinedProbe] Scene ready: two identical combined-high tasks with an explicit confirmation gate.");
    }

    public static void ValidateFromCommandLine()
    {
        if (!File.Exists(Path.GetFullPath(CombinedScenePath)))
            throw new FileNotFoundException("The combined-probe scene was not generated.", CombinedScenePath);

        Scene scene = EditorSceneManager.OpenScene(CombinedScenePath, OpenSceneMode.Single);
        XRCombinedProbeSceneController controller =
            UnityEngine.Object.FindFirstObjectByType<XRCombinedProbeSceneController>();
        if (controller == null)
            throw new InvalidOperationException("The combined-probe scene has no combined controller.");
        if (controller.blockProfiles == null || controller.blockProfiles.Count != 2)
            throw new InvalidOperationException("The combined-probe scene must contain exactly two task blocks.");

        XRWorkloadProbeSceneController.ProbeBlockProfile first = controller.blockProfiles[0];
        XRWorkloadProbeSceneController.ProbeBlockProfile second = controller.blockProfiles[1];
        if (first == null || second == null ||
            string.Equals(first.blockId, second.blockId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Repeated tasks require two distinct block instance IDs.");
        if (!string.Equals(first.taskProfileId, XRCombinedProbeSceneController.SharedTaskProfileId, StringComparison.Ordinal) ||
            !string.Equals(second.taskProfileId, XRCombinedProbeSceneController.SharedTaskProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException("Both repetitions must reference the shared combined task profile.");
        if (!SameTaskParameters(first, second))
            throw new InvalidOperationException("The two combined task repetitions do not have identical parameters.");
        if (!controller.requireParticipantConfirmationBetweenBlocks)
            throw new InvalidOperationException("The participant confirmation gate is disabled.");
        if (controller.randomizeWorkloadBlocks)
            throw new InvalidOperationException("The two-repeat combined scene must use a fixed repetition order.");
        if (!string.Equals(
                controller.questionnaireBankResourcesPath,
                XRCombinedProbeSceneController.QuestionnaireBankResourcesPath,
                StringComparison.Ordinal))
            throw new InvalidOperationException("The combined scene must use its independent questionnaire bank.");

        XRWorkloadProbeBehaviorCollector collector =
            controller.GetComponent<XRWorkloadProbeBehaviorCollector>();
        if (collector == null || collector.probeController != controller || !collector.writeRawSamples)
            throw new InvalidOperationException("The combined scene behavior-data collector is not configured.");

        bool isInBuildSettings = EditorBuildSettings.scenes.Any(entry =>
            entry != null && entry.enabled &&
            string.Equals(entry.path, CombinedScenePath, StringComparison.OrdinalIgnoreCase));
        if (!isInBuildSettings)
            throw new InvalidOperationException("The combined-probe scene is not enabled in Build Settings.");

        Debug.Log("[XRCombinedProbe] Validation passed: 2 identical tasks, unique block IDs, confirmation gate, and data collector.");
    }

    static bool SameTaskParameters(
        XRWorkloadProbeSceneController.ProbeBlockProfile a,
        XRWorkloadProbeSceneController.ProbeBlockProfile b)
    {
        return string.Equals(a.taskProfileId, b.taskProfileId, StringComparison.Ordinal) &&
               string.Equals(a.targetTlxDimension, b.targetTlxDimension, StringComparison.Ordinal) &&
               a.ruleComplexity == b.ruleComplexity &&
               a.targetCount == b.targetCount &&
               a.distractorCount == b.distractorCount &&
               Mathf.Approximately(a.targetDistance, b.targetDistance) &&
               Mathf.Approximately(a.targetSize, b.targetSize) &&
               Mathf.Approximately(a.timeLimitSeconds, b.timeLimitSeconds) &&
               Mathf.Approximately(a.successThresholdStrictness, b.successThresholdStrictness) &&
               Mathf.Approximately(a.feedbackDelaySeconds, b.feedbackDelaySeconds) &&
               Mathf.Approximately(a.controlNoiseDegrees, b.controlNoiseDegrees) &&
               a.trialsPerBlock == b.trialsPerBlock;
    }
}
