using System;
using System.Collections.Generic;

namespace PAXSMStudyAgent;

internal sealed class AgentResult
{
	public StudyConfig Config { get; init; } = new StudyConfig();

	public string Source { get; init; } = "Local rules";

	public string Summary { get; init; } = "";

	public List<string> Warnings { get; init; } = new List<string>();

	public bool NeedsClarification { get; init; }

	public List<string> Questions { get; init; } = new List<string>();

	public List<StudyConfig> TaskConfigs { get; init; } = new List<StudyConfig>();

	public string QuestionsAsText()
	{
		if (Questions.Count == 0)
		{
			return "";
		}
		List<string> list = new List<string>();
		for (int i = 0; i < Questions.Count; i++)
		{
			list.Add($"{i + 1}. {Questions[i]}");
		}
		return string.Join(Environment.NewLine, list);
	}
}
