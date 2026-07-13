namespace PAXSMStudyAgent;

internal sealed class UnityQuestionnaireSlotInfo
{
	public string SlotId { get; init; } = "";

	public string InsertionPointId { get; set; } = "";

	public string BlockId { get; init; } = "";

	public override string ToString()
	{
		string context = string.IsNullOrWhiteSpace(BlockId) ? "scene" : BlockId;
		return $"{SlotId}  ({context} / {InsertionPointId})";
	}
}
