namespace PAXSMStudyAgent;

internal sealed class SceneMeasurementSchedule
{
	public string schemaVersion { get; set; } = "carexr-scene-schedule-v1";

	public string sceneId { get; set; } = "";

	public string scenePath { get; set; } = "";

	public string generatedAtUtc { get; set; } = "";

	public List<SceneQuestionnaireAssignment> assignments { get; set; } = new List<SceneQuestionnaireAssignment>();
}

internal sealed class SceneQuestionnaireAssignment
{
	public string slotId { get; set; } = "";

	public string insertionPointId { get; set; } = "";

	public string blockId { get; set; } = "";

	public string questionnaireId { get; set; } = "";

	public string questionnaireBankResourcesPath { get; set; } = "";

	public string construct { get; set; } = "";

	public string instrumentName { get; set; } = "";

	public int scale { get; set; }

	public string responseMode { get; set; } = "";

	public bool confidenceEnabled { get; set; }
}
