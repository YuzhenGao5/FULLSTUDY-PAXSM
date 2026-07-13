using System;
using System.Collections.Generic;

namespace PAXSMStudyAgent;

internal sealed class ProjectValidationResult
{
	public List<string> Ok { get; } = new List<string>();

	public List<string> Warnings { get; } = new List<string>();

	public List<string> Errors { get; } = new List<string>();

	public bool IsReady => Errors.Count == 0;

	public string ToReport()
	{
		List<string> list = new List<string>();
		list.Add(IsReady ? "Project status: READY" : "Project status: NEEDS ATTENTION");
		list.Add("");
		foreach (string item in Ok)
		{
			list.Add("[OK] " + item);
		}
		foreach (string warning in Warnings)
		{
			list.Add("[WARN] " + warning);
		}
		foreach (string error in Errors)
		{
			list.Add("[ERROR] " + error);
		}
		return string.Join(Environment.NewLine, list);
	}
}
