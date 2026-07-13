using System.Text;

namespace PAXSMStudyAgent;

internal sealed class UnitySceneInfo
{
	public string Name { get; init; } = "";

	public string RelativePath { get; init; } = "";

	public string FullPath { get; init; } = "";

	public bool IsInBuildSettings { get; init; }

	public bool IsBuildEnabled { get; init; }

	public int BuildIndex { get; init; } = -1;

	public string SceneRole { get; init; } = "Unity scene";

	public bool SupportsPaxsm { get; init; }

	public string QuestionnaireBankPath { get; init; } = "";

	public IReadOnlyList<string> BlockIds { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> QuestionnaireSlotIds { get; init; } = Array.Empty<string>();

	public IReadOnlyList<UnityQuestionnaireSlotInfo> QuestionnaireSlots { get; init; } = Array.Empty<UnityQuestionnaireSlotInfo>();

	public IReadOnlyList<string> PublishedAssignments { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> InsertionPoints { get; init; } = Array.Empty<string>();

	public string BuildStatus => IsBuildEnabled
		? $"Enabled (index {BuildIndex})"
		: IsInBuildSettings ? "Listed but disabled" : "Not listed";

	public string Details()
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine($"Scene: {Name}");
		builder.AppendLine($"Path: {RelativePath}");
		builder.AppendLine($"Role: {SceneRole}");
		builder.AppendLine($"Build Settings: {BuildStatus}");
		builder.AppendLine($"PAXSM runtime detected: {(SupportsPaxsm ? "Yes" : "No")}");
		builder.AppendLine($"Questionnaire bank: {(string.IsNullOrWhiteSpace(QuestionnaireBankPath) ? "Not declared in scene" : QuestionnaireBankPath)}");
		builder.AppendLine("Blocks:");
		if (BlockIds.Count == 0)
		{
			builder.AppendLine("- None declared");
		}
		else
		{
			foreach (string blockId in BlockIds)
			{
				builder.AppendLine("- " + blockId);
			}
		}
		builder.AppendLine("Questionnaire slots:");
		if (QuestionnaireSlotIds.Count == 0)
		{
			builder.AppendLine("- None declared");
		}
		else
		{
			foreach (string slotId in QuestionnaireSlotIds)
			{
				builder.AppendLine("- " + slotId);
			}
		}
		builder.AppendLine("Published assignments:");
		if (PublishedAssignments.Count == 0)
		{
			builder.AppendLine("- None");
		}
		else
		{
			foreach (string assignment in PublishedAssignments)
			{
				builder.AppendLine("- " + assignment);
			}
		}
		builder.AppendLine("Insertion points:");
		if (InsertionPoints.Count == 0)
		{
			builder.AppendLine("- None declared");
		}
		else
		{
			foreach (string point in InsertionPoints)
			{
				builder.AppendLine("- " + point);
			}
		}
		builder.AppendLine();
		builder.AppendLine("Read-only discovery. Selecting this scene does not modify the Unity project.");
		return builder.ToString().TrimEnd();
	}

	public override string ToString()
	{
		string buildMarker = IsBuildEnabled ? "[Build]" : IsInBuildSettings ? "[Disabled]" : "[Scene]";
		return $"{buildMarker} {Name}  -  {SceneRole}";
	}
}
