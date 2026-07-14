using UnityEngine;

public sealed class XRQuestionnaireReadSceneController : XRWorkloadProbeSceneController
{
    protected override void Awake()
    {
        questionnaireOnlyMode = true;
        requireReadAcknowledgement = true;
        collectQuestionnaireBetweenBlocks = true;
        collectConfidenceAfterEachItem = true;
        recordQuestionnairePersonalSpeed = true;

        if (string.IsNullOrWhiteSpace(conditionLabel))
            conditionLabel = "QuestionnaireRead";
        if (string.IsNullOrWhiteSpace(questionnaireOnlySessionId))
            questionnaireOnlySessionId = "standalone_questionnaire";
        if (string.IsNullOrWhiteSpace(outputFolderName) || outputFolderName == "XRWorkloadProbe_Data")
            outputFolderName = "XRQuestionnaireRead_Data";

        base.Awake();
    }
}
