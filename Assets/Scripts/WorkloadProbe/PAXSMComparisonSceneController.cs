using UnityEngine;

public sealed class PAXSMComparisonSceneController : XRQuestionnaireReadSceneController
{
    protected override void Awake()
    {
        questionnaireComparisonMode = true;
        questionnaireOnlyMode = true;
        requireReadAcknowledgement = true;
        collectQuestionnaireBetweenBlocks = true;
        collectConfidenceAfterEachItem = true;
        comparisonCollectConfidence = true;
        recordQuestionnairePersonalSpeed = true;
        questionnaireInputMethod = QuestionnaireInputMethod.PaxsmKnob;

        conditionLabel = "PAXSMComparison";
        questionnaireOnlySessionId = "paxsm_comparison";
        outputFolderName = "PAXSMComparison_Data";
        comparisonWorkloadBankResourcesPath = "QuestionBanks/NASA_TLX_21_Comparison";
        comparisonSusBankResourcesPath = "QuestionBanks/SUS";
        comparisonInputCheckBankResourcesPath = "QuestionBanks/Comparison_Input_Check_21";
        comparisonArithmeticTrialsPerMethod = 4;

        base.Awake();
    }
}
