using System.Collections.Generic;

/// <summary>
/// Runs two identical instances of the calibrated combined-high task. The block IDs
/// identify the repetitions while taskProfileId keeps their task definition shared.
/// </summary>
public sealed class XRCombinedProbeSceneController : XRWorkloadProbeSceneController
{
    public const string SharedTaskProfileId = "combined_high_v1";
    public const string QuestionnaireBankResourcesPath = "QuestionBanks/CombinedProbe_NASA_TLX_21";

    protected override void Awake()
    {
        ConfigureCombinedExperiment();
        base.Awake();
    }

    public void ConfigureCombinedExperiment()
    {
        questionnaireOnlyMode = false;
        requireReadAcknowledgement = false;
        conditionLabel = "CombinedProbeRepetition";
        outputFolderName = "XRCombinedProbe_Data";
        questionnaireBankResourcesPath = QuestionnaireBankResourcesPath;
        randomizeWorkloadBlocks = false;
        startAutomatically = true;
        writeCsvOnQuit = true;

        collectQuestionnaireBetweenBlocks = true;
        collectConfidenceAfterEachItem = true;
        recordQuestionnairePersonalSpeed = true;

        requireParticipantConfirmationBetweenBlocks = true;
        interBlockConfirmationPrompt =
            "Combined task 1 and its questionnaire are complete. " +
            "When you are ready, press A on the right controller to begin combined task 2.";

        blockProfiles = new List<ProbeBlockProfile>
        {
            CreateCombinedProfile(
                "combined_high_repeat_1",
                "Combined-high · repetition 1"),
            CreateCombinedProfile(
                "combined_high_repeat_2",
                "Combined-high · repetition 2")
        };
    }

    static ProbeBlockProfile CreateCombinedProfile(string blockId, string displayName)
    {
        return new ProbeBlockProfile
        {
            blockId = blockId,
            taskProfileId = SharedTaskProfileId,
            displayName = displayName,
            targetTlxDimension = "Effort / overall workload",
            ruleComplexity = 3,
            targetCount = 7,
            distractorCount = 6,
            targetDistance = 2.2f,
            targetSize = 0.16f,
            timeLimitSeconds = 2.5f,
            successThresholdStrictness = 0f,
            feedbackDelaySeconds = 0.35f,
            controlNoiseDegrees = 1.5f,
            trialsPerBlock = 10,
            rationale =
                "Combined cognitive, physical, temporal, and feedback demands should increase perceived effort."
        };
    }
}
