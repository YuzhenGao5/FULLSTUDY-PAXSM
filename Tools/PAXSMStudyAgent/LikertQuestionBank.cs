using System.Collections.Generic;

namespace PAXSMStudyAgent;

internal sealed class LikertQuestionBank
{
	public int version { get; set; } = 1;

	public int scale { get; set; } = 21;

	public string default_mode { get; set; } = "slider";

	public List<string> labels { get; set; } = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" };

	public List<LikertItem> items { get; set; } = new List<LikertItem>();
}
