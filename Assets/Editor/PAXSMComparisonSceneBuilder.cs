using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PAXSMComparisonSceneBuilder
{
    const string SourceScenePath = "Assets/Scenes/XRQuestionnaireReadScene.unity";
    const string TargetScenePath = "Assets/Scenes/PAXSMComparisonScene.unity";

    [MenuItem("CARE-XR/Rebuild PAXSM Comparison Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Build();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
    }

    public static void BuildFromCommandLine()
    {
        Build();
        ExperimentSetupSceneBuilder.BuildFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        if (!File.Exists(Path.GetFullPath(TargetScenePath)))
            throw new FileNotFoundException("Comparison scene is missing.", TargetScenePath);

        Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
        PAXSMComparisonSceneController controller =
            UnityEngine.Object.FindFirstObjectByType<PAXSMComparisonSceneController>();
        if (controller == null)
            throw new InvalidOperationException("Comparison scene has no PAXSMComparisonSceneController.");
        if (!controller.questionnaireComparisonMode || !controller.questionnaireOnlyMode)
            throw new InvalidOperationException("Comparison controller is not in questionnaire comparison mode.");
        if (controller.comparisonFormalTrialsPerMethod < 8 || controller.comparisonFormalTrialsPerMethod > 12)
            throw new InvalidOperationException("Formal target trials must remain between 8 and 12 per method.");
        if (controller.comparisonCollectConfidence || controller.collectConfidenceAfterEachItem)
            throw new InvalidOperationException("Study 1 should not collect the removed confidence stage.");
        if (controller.tutorialRightControllerPrefab == null)
            throw new InvalidOperationException("Comparison scene lost the controller tutorial prefab reference.");
        if (GameObject.Find("XR Origin (XR Rig)") == null)
            throw new InvalidOperationException("Comparison scene has no XR Origin.");

        TextAsset sus = Resources.Load<TextAsset>("QuestionBanks/SUS");
        if (sus == null || string.IsNullOrWhiteSpace(sus.text) || !sus.text.Contains("sus_10_training"))
            throw new InvalidOperationException("The 10-item SUS question bank is unavailable.");
        TextAsset workload = Resources.Load<TextAsset>("QuestionBanks/NASA_TLX_21_Comparison");
        if (workload == null || string.IsNullOrWhiteSpace(workload.text) ||
            !workload.text.Contains("nasa_tlx_frustration"))
            throw new InvalidOperationException("The locked six-item NASA-TLX comparison bank is unavailable.");
        TextAsset practice = Resources.Load<TextAsset>("QuestionBanks/Comparison_Practice_Targets_21");
        if (practice == null || string.IsNullOrWhiteSpace(practice.text) ||
            !practice.text.Contains("practice_target_05") ||
            !practice.text.Contains("practice_target_17"))
            throw new InvalidOperationException("The two-item comparison practice bank is unavailable.");
        TextAsset formalA = Resources.Load<TextAsset>("QuestionBanks/Comparison_Formal_Targets_A_21");
        TextAsset formalB = Resources.Load<TextAsset>("QuestionBanks/Comparison_Formal_Targets_B_21");
        if (formalA == null || formalB == null ||
            !formalA.text.Contains("formal_target_01") || !formalA.text.Contains("formal_target_21") ||
            !formalB.text.Contains("formal_target_01") || !formalB.text.Contains("formal_target_21"))
            throw new InvalidOperationException("The counterbalanced 12-item formal target banks are unavailable.");

        bool inBuildSettings = EditorBuildSettings.scenes.Any(entry =>
            entry.enabled && string.Equals(entry.path, TargetScenePath, StringComparison.OrdinalIgnoreCase));
        if (!inBuildSettings)
            throw new InvalidOperationException("Comparison scene is not enabled in Build Settings.");

        Debug.Log("[PAXSM Comparison] Validation passed: single-factor design, XR rig, questionnaire banks, and Build Settings are ready.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    static void Build()
    {
        if (!File.Exists(Path.GetFullPath(SourceScenePath)))
            throw new FileNotFoundException("Questionnaire read source scene is missing.", SourceScenePath);

        Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        XRQuestionnaireReadSceneController source =
            UnityEngine.Object.FindFirstObjectByType<XRQuestionnaireReadSceneController>();
        if (source == null)
            throw new InvalidOperationException("Source scene has no XRQuestionnaireReadSceneController.");

        GameObject host = source.gameObject;
        GameObject tutorialPrefab = source.tutorialRightControllerPrefab;
        float settleSeconds = source.headPoseSettleSeconds;
        float forwardOffset = source.readSceneKnobForwardOffset;
        float rightOffset = source.readSceneKnobRightOffset;
        float belowEyeOffset = source.readSceneKnobBelowEyeOffset;
        float minimumHeight = source.readSceneKnobMinimumHeight;
        float maximumHeight = source.readSceneKnobMaximumHeight;
        Vector3 tutorialPosition = source.tutorialControllerTablePosition;
        Vector3 tutorialEuler = source.tutorialControllerEuler;
        float tutorialClearance = source.tutorialControllerTableClearance;
        float tutorialScale = source.tutorialControllerScale;
        float tutorialCycle = source.tutorialCycleSeconds;

        UnityEngine.Object.DestroyImmediate(source);
        PAXSMComparisonSceneController controller = host.AddComponent<PAXSMComparisonSceneController>();
        host.name = "PAXSM Comparison Bootstrap";

        controller.questionnaireComparisonMode = true;
        controller.questionnaireOnlyMode = true;
        controller.requireReadAcknowledgement = true;
        controller.participantId = "P001";
        controller.sessionNumber = 1;
        controller.conditionLabel = "PAXSMComparison";
        controller.questionnaireOnlySessionId = "paxsm_comparison";
        controller.outputFolderName = "PAXSMComparison_Data";
        controller.startAutomatically = true;
        controller.writeCsvOnQuit = true;
        controller.collectQuestionnaireBetweenBlocks = true;
        controller.collectConfidenceAfterEachItem = false;
        controller.comparisonCollectConfidence = false;
        controller.comparisonFormalTrialsPerMethod = 12;
        controller.comparisonRestSeconds = 20f;
        controller.comparisonPointClickItemsPerPage = 1;
        controller.comparisonPointClickRadioDiameterMeters = 0.055f;
        controller.comparisonWorkloadBankResourcesPath = "QuestionBanks/NASA_TLX_21_Comparison";
        controller.comparisonSusBankResourcesPath = "QuestionBanks/SUS";
        controller.comparisonPracticeBankResourcesPath = "QuestionBanks/Comparison_Practice_Targets_21";
        controller.comparisonFormalBankAResourcesPath = "QuestionBanks/Comparison_Formal_Targets_A_21";
        controller.comparisonFormalBankBResourcesPath = "QuestionBanks/Comparison_Formal_Targets_B_21";
        controller.questionnaireBankResourcesPath = "QuestionBanks/NASA_TLX_21_Comparison";
        controller.questionnaireScale = 21;
        controller.questionnaireConfidenceScale = 5;
        controller.questionnaireInputMethod = XRWorkloadProbeSceneController.QuestionnaireInputMethod.PaxsmKnob;
        controller.questionnaireKnobArcDegrees = 120f;
        controller.questionnairePanelTiltDegrees = 18f;
        controller.questionnaireConfirmHoldSeconds = 0.8f;
        controller.recordQuestionnairePersonalSpeed = true;

        controller.headPoseSettleSeconds = settleSeconds;
        controller.readSceneKnobForwardOffset = forwardOffset;
        controller.readSceneKnobRightOffset = rightOffset;
        controller.readSceneKnobBelowEyeOffset = belowEyeOffset;
        controller.readSceneKnobMinimumHeight = minimumHeight;
        controller.readSceneKnobMaximumHeight = maximumHeight;
        controller.showFirstGrabGuidance = true;
        controller.showControllerTutorial = true;
        controller.tutorialRightControllerPrefab = tutorialPrefab;
        controller.tutorialControllerTablePosition = tutorialPosition;
        controller.tutorialControllerEuler = tutorialEuler;
        controller.tutorialControllerTableClearance = tutorialClearance;
        controller.tutorialControllerScale = tutorialScale;
        controller.tutorialCycleSeconds = tutorialCycle;

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, TargetScenePath, true))
            throw new InvalidOperationException($"Could not save {TargetScenePath}.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Debug.Log($"[PAXSM Comparison] Scene rebuilt from {SourceScenePath}: {TargetScenePath}");
    }
}
