using System;
using System.Collections.Generic;
using System.IO;

namespace PAXSMStudyAgent;

internal static class StudyPaths
{
	public static string DefaultOutputFolder
	{
		get
		{
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			string root = string.IsNullOrWhiteSpace(desktop)
				? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
				: desktop;
			return Path.Combine(root, "CARE-XR Data");
		}
	}
}

internal sealed class StudyConfig
{
	public string schemaVersion { get; set; } = "paxsm-study-config-v1";

	public string studyName { get; set; } = "PAXSM Full Study";

	public string studyVersion { get; set; } = "1.0";

	public string design { get; set; } = "within-subjects";

	public string construct { get; set; } = "cognitive_load";

	public string participantId { get; set; } = "P001";

	public int participantNumber { get; set; } = 1;

	public int sessionNumber { get; set; } = 1;

	public string experimenterId { get; set; } = "";

	public int conditionIndex { get; set; } = 1;

	public string conditionLabel { get; set; } = "CognitiveLoad";

	public List<string> conditions { get; set; } = new List<string>();

	public string counterbalancingOrder { get; set; } = "A";

	public bool randomizeQuestions { get; set; }

	public string questionBankResourcesPath { get; set; } = "QuestionBanks/Scale";

	public string responseMode { get; set; } = "slider";

	public int scale { get; set; } = 21;

	public string recommendationRole { get; set; } = "";

	public string instrumentName { get; set; } = "";

	public string instrumentStatus { get; set; } = "";

	public string recommendedStandardInstrument { get; set; } = "";

	public string recommendationRationale { get; set; } = "";

	public string outputFolder { get; set; } = StudyPaths.DefaultOutputFolder;

	public string outputSubfolder { get; set; } = "ExportsCSV";

	public string fileNamePrefix { get; set; } = "PAXSM";

	public bool exportMergedCsv { get; set; } = true;

	public bool exportRawStageEvents { get; set; } = true;

	public bool exportOnQuit { get; set; } = true;

	public bool preventOverwrite { get; set; } = true;

	public string naturalLanguageRequest { get; set; } = "";

	public string generatedSummary { get; set; } = "";
}
