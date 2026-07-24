using UnityEngine;

public sealed class PAXSMComparisonSceneController : XRQuestionnaireReadSceneController
{
    protected override void Awake()
    {
        questionnaireComparisonMode = true;
        questionnaireOnlyMode = true;
        personalKnobReferenceMode = false;
        requireReadAcknowledgement = true;
        collectQuestionnaireBetweenBlocks = true;
        collectConfidenceAfterEachItem = false;
        comparisonCollectConfidence = false;
        recordQuestionnairePersonalSpeed = true;
        questionnaireInputMethod = QuestionnaireInputMethod.PaxsmKnob;

        conditionLabel = "PAXSMComparison";
        questionnaireOnlySessionId = "paxsm_comparison";
        outputFolderName = "PAXSMComparison_Data";
        comparisonWorkloadBankResourcesPath = "QuestionBanks/NASA_TLX_21_Comparison";
        comparisonSusBankResourcesPath = "QuestionBanks/SUS";
        comparisonPracticeBankResourcesPath = "QuestionBanks/Comparison_Practice_Targets_21";
        comparisonFormalBankAResourcesPath = "QuestionBanks/Comparison_Formal_Targets_A_21";
        comparisonFormalBankBResourcesPath = "QuestionBanks/Comparison_Formal_Targets_B_21";
        comparisonFormalTrialsPerMethod = 12;
        comparisonRestSeconds = 20f;

        base.Awake();
    }
}
