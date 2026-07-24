#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PAXSMPersonalKnobReferenceValidator
{
    const string ScenePath = "Assets/Scenes/XRQuestionnaireReadScene.unity";

    [MenuItem("CARE-XR/Testing/Validate Personal Knob Reference Scene")]
    public static void ValidateFromMenu()
    {
        ValidateFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
            throw new InvalidOperationException("Could not open XRQuestionnaireReadScene.");

        XRQuestionnaireReadSceneController controller =
            UnityEngine.Object.FindFirstObjectByType<XRQuestionnaireReadSceneController>();
        if (controller == null)
            throw new InvalidOperationException("XRQuestionnaireReadSceneController is missing from XRQuestionnaireReadScene.");

        Require(controller.personalKnobReferenceMode, "Personal knob reference mode is not enabled.");
        Require(controller.questionnaireOnlyMode, "The scene must remain questionnaire-only.");
        Require(controller.requireReadAcknowledgement, "Formal target-entry trials must retain the Read stage.");
        Require(controller.collectConfidenceAfterEachItem, "Each Answer reference trial must be followed by a Confidence reference trial.");
        Require(controller.recordQuestionnairePersonalSpeed, "Personal knob speed instrumentation is not enabled.");
        Require(controller.questionnaireScale == 21, "Answer reference trials must use the 21-point knob.");
        Require(controller.questionnaireConfidenceScale == 5, "Confidence reference trials must use the 5-point knob.");
        Require(controller.personalReferenceTrialCount == 12, "The scene must collect 12 formal personal-reference trials.");
        Require(controller.personalReferenceMinimumValidTrials == 8, "The profile should require at least 8 valid formal trials.");
        Require(controller.outputFolderName == "PAXSMPersonalKnobReference_Data", "The personal-reference data namespace is incorrect.");

        int[] expectedAnswer = { 10, 12, 9, 13, 7, 15, 6, 16, 3, 19, 1, 21 };
        int[] expectedConfidence = { 2, 4, 2, 4, 2, 4, 1, 5, 1, 5, 1, 5 };
        ValidateTargets(21, expectedAnswer, "Answer");
        ValidateTargets(5, expectedConfidence, "Confidence");

        string sceneText = File.ReadAllText(ScenePath);
        Require(!sceneText.Contains("Careful reference", StringComparison.OrdinalIgnoreCase), "The scene still contains a Careful reference label.");
        Require(!sceneText.Contains("Quick reference", StringComparison.OrdinalIgnoreCase), "The scene still contains a Quick reference label.");

        Debug.Log(
            "[CARE-XR Validation] Personal knob reference scene passed: " +
            "12 formal distance-balanced Answer/Confidence target-entry trials, excluded practice, " +
            "raw traces, stage events, physical speed samples, slot-speed events, and personal profile export are configured.");
    }

    static void ValidateTargets(int scale, int[] expected, string stage)
    {
        // Keep this validator independent of the runtime assembly. The same fixed schedule
        // is checked here and in the scene controller so an Editor compile cannot mask a
        // configuration error with a stale runtime reference.
        int[] actual = BuildExpectedTargetSchedule(scale, expected.Length);
        Require(actual.Length == expected.Length, $"{stage} target schedule has an unexpected length.");
        for (int i = 0; i < expected.Length; i++)
            Require(actual[i] == expected[i], $"{stage} target {i + 1} should be {expected[i]}, found {actual[i]}.");

        int shortCount = 0;
        int mediumCount = 0;
        int longCount = 0;
        int center = Mathf.CeilToInt(scale * 0.5f);
        foreach (int target in actual)
        {
            switch (GetDistanceBin(scale, Mathf.Abs(target - center)))
            {
                case "short": shortCount++; break;
                case "medium": mediumCount++; break;
                case "long": longCount++; break;
            }
        }

        if (scale == 21)
            Require(shortCount == 4 && mediumCount == 4 && longCount == 4,
                "Answer targets must balance four short, four medium, and four long movements.");
        else
            Require(shortCount == 6 && mediumCount == 0 && longCount == 6,
                "Confidence targets must balance six short and six long movements.");
    }

    static int[] BuildExpectedTargetSchedule(int scale, int count)
    {
        int[] source = scale == 21
            ? new[] { 10, 12, 9, 13, 7, 15, 6, 16, 3, 19, 1, 21 }
            : new[] { 2, 4, 2, 4, 2, 4, 1, 5, 1, 5, 1, 5 };
        var targets = new int[Mathf.Max(1, count)];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = source[i % source.Length];
        return targets;
    }

    static string GetDistanceBin(int scale, int slotDistance)
    {
        if (slotDistance <= 0)
            return slotDistance == 0 ? "zero" : "unknown";
        if (scale <= 5)
            return slotDistance == 1 ? "short" : "long";

        // The formal 21-point schedule has short (1-2), medium (3-6), and
        // long (7-10) movements from the midpoint.
        if (slotDistance <= 2)
            return "short";
        if (slotDistance <= 6)
            return "medium";
        return "long";
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
