using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PAXSMStudyAgent;

internal sealed class ProjectConfigurator
{
	private readonly AgentEngine _agentEngine;

	public string ProjectRoot { get; private set; } = "";

	public ProjectConfigurator(AgentEngine agentEngine)
	{
		_agentEngine = agentEngine;
	}

	public bool TryLocateProjectRoot(string startDirectory)
	{
		for (DirectoryInfo directoryInfo = new DirectoryInfo(startDirectory); directoryInfo != null; directoryInfo = directoryInfo.Parent)
		{
			if (Directory.Exists(Path.Combine(directoryInfo.FullName, "Assets")) && Directory.Exists(Path.Combine(directoryInfo.FullName, "ProjectSettings")) && Directory.Exists(Path.Combine(directoryInfo.FullName, "Packages")))
			{
				ProjectRoot = directoryInfo.FullName;
				return true;
			}
		}
		return false;
	}

	public void SetProjectRoot(string root)
	{
		ProjectRoot = root;
	}

	public string Apply(StudyConfig config)
	{
		EnsureProjectRoot();
		List<string> list = new List<string>();
		WriteFormalConfig(config, list);
		WriteQuestionBank(config, list);
		EnsureBuildSettingsMainScene(list);
		TryCreateOutputFolder(config, list);
		ProjectValidationResult projectValidationResult = Validate();
		list.Add("");
		list.Add(projectValidationResult.ToReport());
		return string.Join(Environment.NewLine, list);
	}

	public ProjectValidationResult Validate()
	{
		EnsureProjectRoot();
		ProjectValidationResult projectValidationResult = new ProjectValidationResult();
		CheckFile(projectValidationResult, "Assets/Scenes/MainScene.unity", "MainScene exists.");
		CheckFile(projectValidationResult, "ProjectSettings/EditorBuildSettings.asset", "EditorBuildSettings exists.");
		CheckFile(projectValidationResult, "Assets/Resources/QuestionBanks/Scale.json", "Question bank exists.");
		CheckFile(projectValidationResult, "Assets/Resources/StudyConfigs/FormalStudyConfig.json", "Formal study config exists.");
		CheckFile(projectValidationResult, "Assets/Scripts/PAXSMStudyConfigRuntimeApplier.cs", "Runtime config applier exists.");
		CheckFile(projectValidationResult, "Assets/Scripts/Interface/DataLoader.cs", "Merged CSV exporter script exists.");
		CheckFile(projectValidationResult, "Assets/DTATTIME.cs", "Stage-event reporter script exists.");
		if (ReadIfExists("ProjectSettings/EditorBuildSettings.asset").Contains("Assets/Scenes/MainScene.unity", StringComparison.OrdinalIgnoreCase))
		{
			projectValidationResult.Ok.Add("Build Settings points to Assets/Scenes/MainScene.unity.");
		}
		else
		{
			projectValidationResult.Errors.Add("Build Settings does not point to Assets/Scenes/MainScene.unity.");
		}
		string text = ReadIfExists("Assets/Scenes/MainScene.unity");
		CheckSceneToken(projectValidationResult, text, "ALLCONTROL", "ALLCONTROL component appears in MainScene.");
		CheckSceneToken(projectValidationResult, text, "SimpleJsonReader", "SimpleJsonReader component appears in MainScene.");
		CheckSceneToken(projectValidationResult, text, "QuestionRenderer", "QuestionRenderer component appears in MainScene.");
		CheckSceneToken(projectValidationResult, text, "DataLoader", "DataLoader object appears in MainScene.");
		CheckSceneToken(projectValidationResult, text, "participantNumber", "Exporter participant/session fields appear in MainScene.");
		CheckSceneToken(projectValidationResult, text, "AllControlStageEventReporter", "Stage-event reporter appears in MainScene.");
		if (text.Contains("Missing Prefab", StringComparison.OrdinalIgnoreCase))
		{
			projectValidationResult.Warnings.Add("MainScene contains text that looks like a missing prefab reference. Open Unity and resolve it before running participants.");
		}
		ValidateQuestionBank(projectValidationResult);
		return projectValidationResult;
	}

	public string PrettyJson(StudyConfig config)
	{
		return _agentEngine.ToJson(config);
	}

	public IReadOnlyList<UnitySceneInfo> DiscoverScenes()
	{
		EnsureProjectRoot();
		string scenesRoot = Path.Combine(ProjectRoot, "Assets", "Scenes");
		if (!Directory.Exists(scenesRoot))
		{
			return Array.Empty<UnitySceneInfo>();
		}

		Dictionary<string, (bool Enabled, int BuildIndex)> buildScenes = ReadBuildScenes();
		List<UnitySceneInfo> scenes = new List<UnitySceneInfo>();
		foreach (string scenePath in Directory.EnumerateFiles(scenesRoot, "*.unity", SearchOption.AllDirectories)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			string relativePath = Path.GetRelativePath(ProjectRoot, scenePath).Replace('\\', '/');
			string sceneText = File.ReadAllText(scenePath);
			bool isMainScene = string.Equals(Path.GetFileNameWithoutExtension(scenePath), "MainScene", StringComparison.OrdinalIgnoreCase);
			bool isTemplate = string.Equals(Path.GetFileNameWithoutExtension(scenePath), "Template", StringComparison.OrdinalIgnoreCase);
			bool hasTemplateContract = sceneText.Contains("insertionPointId:", StringComparison.OrdinalIgnoreCase) ||
				sceneText.Contains("PAXSM Empty Template Bootstrap", StringComparison.OrdinalIgnoreCase);
			bool hasEmbeddedQuestionnaire = sceneText.Contains("questionnaireBankResourcesPath:", StringComparison.OrdinalIgnoreCase) ||
				hasTemplateContract;
			bool hasStandaloneQuestionnaire = isMainScene ||
				sceneText.Contains("QuestionRenderer", StringComparison.OrdinalIgnoreCase) ||
				sceneText.Contains("ALLCONTROL", StringComparison.OrdinalIgnoreCase);
			bool supportsPaxsm = hasStandaloneQuestionnaire || hasEmbeddedQuestionnaire;
			string role = hasStandaloneQuestionnaire
				? "Standalone PAXSM"
				: hasEmbeddedQuestionnaire && isTemplate
					? "Embedded PAXSM template"
					: hasEmbeddedQuestionnaire ? "Embedded PAXSM / probe" : "Unity scene";

			Match bankMatch = Regex.Match(sceneText, @"(?m)^[ \t]*questionnaireBankResourcesPath:[ \t]*(?<path>[^\r\n]*)$", RegexOptions.IgnoreCase);
			string bankPath = bankMatch.Success ? bankMatch.Groups["path"].Value.Trim() : "";
			List<string> blockIds = Regex.Matches(sceneText, @"(?m)^\s*-?\s*blockId:\s*(?<id>[^\r\n]+)$", RegexOptions.IgnoreCase)
				.Cast<Match>()
				.Select(match => match.Groups["id"].Value.Trim())
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			List<UnityQuestionnaireSlotInfo> questionnaireSlots = ReadQuestionnaireSlots(sceneText);
			List<string> questionnaireSlotIds = questionnaireSlots.Select(slot => slot.SlotId).ToList();
			List<string> insertionPoints = new List<string>();
			if (hasStandaloneQuestionnaire)
			{
				insertionPoints.Add("standalone_flow (scene-owned questionnaire sequence)");
			}
			if (hasEmbeddedQuestionnaire && Regex.IsMatch(sceneText, @"(?m)^\s*collectQuestionnaireBetweenBlocks:\s*1\s*$"))
			{
				insertionPoints.Add("after_block (current hardcoded trigger)");
			}
			foreach (string point in Regex.Matches(sceneText, @"(?m)^\s*insertionPointId:\s*(?<id>[^\r\n]+)$", RegexOptions.IgnoreCase)
				.Cast<Match>()
				.Select(match => match.Groups["id"].Value.Trim())
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				insertionPoints.Add(point + " (declared questionnaire slot)");
			}

			bool isListed = buildScenes.TryGetValue(relativePath, out (bool Enabled, int BuildIndex) build);
			string sceneName = Path.GetFileNameWithoutExtension(scenePath);
			SceneMeasurementSchedule publishedSchedule = ReadSceneSchedule(sceneName);
			List<string> publishedAssignments = publishedSchedule.assignments
				.Select(assignment => $"{assignment.slotId} -> {DisplayQuestionnaireName(assignment)} ({assignment.scale}-point {assignment.responseMode})")
				.ToList();
			scenes.Add(new UnitySceneInfo
			{
				Name = sceneName,
				RelativePath = relativePath,
				FullPath = scenePath,
				IsInBuildSettings = isListed,
				IsBuildEnabled = isListed && build.Enabled,
				BuildIndex = isListed ? build.BuildIndex : -1,
				SceneRole = role,
				SupportsPaxsm = supportsPaxsm,
				QuestionnaireBankPath = bankPath,
				BlockIds = blockIds,
				QuestionnaireSlotIds = questionnaireSlotIds,
				QuestionnaireSlots = questionnaireSlots,
				PublishedAssignments = publishedAssignments,
				InsertionPoints = insertionPoints
			});
		}

		return scenes;
	}

	public string AssignQuestionnaireToSlot(UnitySceneInfo scene, UnityQuestionnaireSlotInfo slot, StudyConfig config)
	{
		EnsureProjectRoot();
		if (!scene.QuestionnaireSlotIds.Contains(slot.SlotId, StringComparer.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Scene {scene.Name} does not declare slot {slot.SlotId}.");
		}

		string questionnaireId = SafeFileToken(
			!string.IsNullOrWhiteSpace(config.instrumentName) ? config.instrumentName : config.construct,
			"questionnaire");
		string sceneToken = SafeFileToken(scene.Name, "scene");
		string slotToken = SafeFileToken(slot.SlotId, "slot");
		string resourcePath = $"PAXSM/SceneQuestionBanks/{sceneToken}/{slotToken}_{questionnaireId}";
		string bankPath = Path.Combine(
			ProjectRoot,
			"Assets",
			"Resources",
			resourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(bankPath)!);
		string bankJson = JsonSerializer.Serialize(_agentEngine.BuildQuestionBank(config), JsonOptions());
		File.WriteAllText(bankPath, bankJson);

		SceneMeasurementSchedule schedule = ReadSceneSchedule(scene.Name);
		schedule.sceneId = scene.Name;
		schedule.scenePath = scene.RelativePath;
		schedule.generatedAtUtc = DateTime.UtcNow.ToString("O");
		schedule.assignments.RemoveAll(item => string.Equals(item.slotId, slot.SlotId, StringComparison.OrdinalIgnoreCase));
		schedule.assignments.Add(new SceneQuestionnaireAssignment
		{
			slotId = slot.SlotId,
			insertionPointId = slot.InsertionPointId,
			blockId = slot.BlockId,
			questionnaireId = questionnaireId,
			questionnaireBankResourcesPath = resourcePath,
			construct = config.construct,
			instrumentName = config.instrumentName,
			scale = config.scale,
			responseMode = config.scale == 21 ? "slider" : config.responseMode,
			confidenceEnabled = false
		});
		WriteSceneSchedule(schedule);
		return $"Published {DisplayQuestionnaireName(schedule.assignments[^1])} to {scene.Name} / {slot.SlotId}." +
			Environment.NewLine + $"Question bank: Resources/{resourcePath}.json" +
			Environment.NewLine + $"Schedule: {SceneSchedulePath(scene.Name)}";
	}

	public string RemoveQuestionnaireFromSlot(UnitySceneInfo scene, UnityQuestionnaireSlotInfo slot)
	{
		EnsureProjectRoot();
		SceneMeasurementSchedule schedule = ReadSceneSchedule(scene.Name);
		int removed = schedule.assignments.RemoveAll(item => string.Equals(item.slotId, slot.SlotId, StringComparison.OrdinalIgnoreCase));
		if (removed == 0)
		{
			return $"No published questionnaire was assigned to {scene.Name} / {slot.SlotId}.";
		}
		schedule.generatedAtUtc = DateTime.UtcNow.ToString("O");
		WriteSceneSchedule(schedule);
		return $"Removed the questionnaire assignment from {scene.Name} / {slot.SlotId}. The generated bank was retained for audit history.";
	}

	public SceneMeasurementSchedule ReadSceneSchedule(string sceneName)
	{
		string path = SceneSchedulePath(sceneName);
		if (!File.Exists(path))
		{
			return new SceneMeasurementSchedule { sceneId = sceneName };
		}
		try
		{
			return JsonSerializer.Deserialize<SceneMeasurementSchedule>(File.ReadAllText(path), new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? new SceneMeasurementSchedule { sceneId = sceneName };
		}
		catch
		{
			return new SceneMeasurementSchedule { sceneId = sceneName };
		}
	}

	private void WriteSceneSchedule(SceneMeasurementSchedule schedule)
	{
		string path = SceneSchedulePath(schedule.sceneId);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (File.Exists(path))
		{
			string backupPath = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".bak";
			File.Copy(path, backupPath);
		}
		File.WriteAllText(path, JsonSerializer.Serialize(schedule, JsonOptions()));
	}

	private string SceneSchedulePath(string sceneName)
	{
		return Path.Combine(
			ProjectRoot,
			"Assets",
			"Resources",
			"PAXSM",
			"SceneSchedules",
			SafeFileToken(sceneName, "scene") + ".json");
	}

	private static List<UnityQuestionnaireSlotInfo> ReadQuestionnaireSlots(string sceneText)
	{
		List<UnityQuestionnaireSlotInfo> slots = new List<UnityQuestionnaireSlotInfo>();
		string currentBlockId = "";
		UnityQuestionnaireSlotInfo? currentSlot = null;
		foreach (string line in sceneText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
		{
			Match blockMatch = Regex.Match(line, @"^\s*-\s*blockId:\s*(?<id>.+?)\s*$", RegexOptions.IgnoreCase);
			if (blockMatch.Success)
			{
				currentBlockId = blockMatch.Groups["id"].Value.Trim();
				currentSlot = null;
				continue;
			}
			Match slotMatch = Regex.Match(line, @"^\s*-\s*slotId:\s*(?<id>.+?)\s*$", RegexOptions.IgnoreCase);
			if (slotMatch.Success)
			{
				currentSlot = new UnityQuestionnaireSlotInfo
				{
					SlotId = slotMatch.Groups["id"].Value.Trim(),
					BlockId = currentBlockId
				};
				slots.Add(currentSlot);
				continue;
			}
			Match pointMatch = Regex.Match(line, @"^\s*insertionPointId:\s*(?<id>.+?)\s*$", RegexOptions.IgnoreCase);
			if (pointMatch.Success && currentSlot != null)
			{
				currentSlot.InsertionPointId = pointMatch.Groups["id"].Value.Trim();
			}
		}
		return slots;
	}

	private static string DisplayQuestionnaireName(SceneQuestionnaireAssignment assignment)
	{
		return !string.IsNullOrWhiteSpace(assignment.instrumentName)
			? assignment.instrumentName
			: !string.IsNullOrWhiteSpace(assignment.construct) ? assignment.construct : assignment.questionnaireId;
	}

	private static string SafeFileToken(string value, string fallback)
	{
		string token = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
		token = Regex.Replace(token, @"[^A-Za-z0-9_-]+", "_").Trim('_');
		return string.IsNullOrWhiteSpace(token) ? fallback : token;
	}

	private static JsonSerializerOptions JsonOptions()
	{
		return new JsonSerializerOptions { WriteIndented = true };
	}

	private Dictionary<string, (bool Enabled, int BuildIndex)> ReadBuildScenes()
	{
		Dictionary<string, (bool Enabled, int BuildIndex)> result =
			new Dictionary<string, (bool Enabled, int BuildIndex)>(StringComparer.OrdinalIgnoreCase);
		string settingsPath = Path.Combine(ProjectRoot, "ProjectSettings", "EditorBuildSettings.asset");
		if (!File.Exists(settingsPath))
		{
			return result;
		}

		string settings = File.ReadAllText(settingsPath);
		MatchCollection matches = Regex.Matches(
			settings,
			@"(?ms)-\s*enabled:\s*(?<enabled>[01])\s*\r?\n\s*path:\s*(?<path>[^\r\n]+)");
		int enabledIndex = 0;
		foreach (Match match in matches)
		{
			string path = match.Groups["path"].Value.Trim().Replace('\\', '/');
			bool enabled = match.Groups["enabled"].Value == "1";
			result[path] = (enabled, enabled ? enabledIndex++ : -1);
		}
		return result;
	}

	private void WriteFormalConfig(StudyConfig config, List<string> messages)
	{
		string text = Path.Combine(ProjectRoot, "Assets", "Resources", "StudyConfigs", "FormalStudyConfig.json");
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		string contents = _agentEngine.ToJson(config);
		File.WriteAllText(text, contents);
		messages.Add("Wrote formal study config:");
		messages.Add(text);
		string text2 = Path.Combine(ProjectRoot, "PAXSMStudyConfig.json");
		File.WriteAllText(text2, contents);
		messages.Add("Wrote external runtime config:");
		messages.Add(text2);
	}

	private void WriteQuestionBank(StudyConfig config, List<string> messages)
	{
		string text = NormalizeResourcePath(config.questionBankResourcesPath, "QuestionBanks/Scale");
		string text2 = Path.Combine(ProjectRoot, "Assets", "Resources", text.Replace("/", Path.DirectorySeparatorChar.ToString()) + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(text2));
		string text3 = JsonSerializer.Serialize(_agentEngine.BuildQuestionBank(config), new JsonSerializerOptions
		{
			WriteIndented = true
		});
		if (File.Exists(text2) && !StringEqualsIgnoringLineEndings(File.ReadAllText(text2), text3))
		{
			string text4 = text2 + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".bak";
			File.Copy(text2, text4);
			messages.Add("Backed up previous question bank:");
			messages.Add(text4);
		}
		File.WriteAllText(text2, text3);
		messages.Add("Wrote question bank:");
		messages.Add(text2);
		string text5 = Path.Combine(ProjectRoot, text.Replace("/", Path.DirectorySeparatorChar.ToString()) + ".json");
		Directory.CreateDirectory(Path.GetDirectoryName(text5));
		File.WriteAllText(text5, text3);
		messages.Add("Wrote external runtime question bank:");
		messages.Add(text5);
	}

	private void EnsureBuildSettingsMainScene(List<string> messages)
	{
		string path = Path.Combine(ProjectRoot, "ProjectSettings", "EditorBuildSettings.asset");
		string path2 = Path.Combine(ProjectRoot, "Assets", "Scenes", "MainScene.unity.meta");
		if (!File.Exists(path) || !File.Exists(path2))
		{
			messages.Add("Skipped Build Settings update because EditorBuildSettings or MainScene meta is missing.");
			return;
		}
		string text = File.ReadAllText(path);
		if (text.Contains("Assets/Scenes/MainScene.unity", StringComparison.OrdinalIgnoreCase))
		{
			messages.Add("Build Settings already points to MainScene.");
			return;
		}
		string text2 = ExtractGuid(File.ReadAllText(path2));
		if (string.IsNullOrWhiteSpace(text2))
		{
			messages.Add("Skipped Build Settings update because MainScene GUID could not be read.");
			return;
		}
		string replacement = "$1Assets/Scenes/MainScene.unity$2" + text2;
		string text3 = new Regex("(?ms)(m_Scenes:\\s*\\r?\\n\\s*-\\s*enabled:\\s*1\\s*\\r?\\n\\s*path:\\s*).*(\\r?\\n\\s*guid:\\s*)[a-fA-F0-9]+").Replace(text, replacement, 1);
		if (text3 != text)
		{
			File.WriteAllText(path, text3);
			messages.Add("Updated Build Settings to MainScene.");
		}
		else
		{
			messages.Add("Could not automatically update Build Settings. Please check EditorBuildSettings.asset manually.");
		}
	}

	private void TryCreateOutputFolder(StudyConfig config, List<string> messages)
	{
		try
		{
			Directory.CreateDirectory(config.outputFolder);
			messages.Add("Output folder is available:");
			messages.Add(config.outputFolder);
		}
		catch (Exception ex)
		{
			messages.Add("Could not create output folder now. Unity may still create it later if permissions allow.");
			messages.Add(ex.Message);
		}
	}

	private void ValidateQuestionBank(ProjectValidationResult result)
	{
		string path = Path.Combine(ProjectRoot, "Assets", "Resources", "QuestionBanks", "Scale.json");
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement value;
			int num = ((jsonDocument.RootElement.TryGetProperty("items", out value) && value.ValueKind == JsonValueKind.Array) ? value.GetArrayLength() : 0);
			JsonElement value2;
			int value3;
			int num2 = ((jsonDocument.RootElement.TryGetProperty("scale", out value2) && value2.TryGetInt32(out value3)) ? value3 : 0);
			if (num > 0)
			{
				result.Ok.Add($"Question bank parses successfully with {num} item(s).");
			}
			else
			{
				result.Errors.Add("Question bank has no items.");
			}
			if (num2 > 1)
			{
				result.Ok.Add($"Question bank scale is {num2}.");
			}
			else
			{
				result.Errors.Add("Question bank scale is missing or invalid.");
			}
		}
		catch (Exception ex)
		{
			result.Errors.Add("Question bank JSON is invalid: " + ex.Message);
		}
	}

	private void CheckFile(ProjectValidationResult result, string relativePath, string okMessage)
	{
		if (File.Exists(Path.Combine(ProjectRoot, relativePath)))
		{
			result.Ok.Add(okMessage);
		}
		else
		{
			result.Errors.Add("Missing file: " + relativePath);
		}
	}

	private void CheckSceneToken(ProjectValidationResult result, string scene, string token, string okMessage)
	{
		if (scene.Contains(token, StringComparison.OrdinalIgnoreCase))
		{
			result.Ok.Add(okMessage);
		}
		else
		{
			result.Warnings.Add("Could not confirm in MainScene: " + okMessage);
		}
	}

	private string ReadIfExists(string relativePath)
	{
		string path = Path.Combine(ProjectRoot, relativePath);
		if (!File.Exists(path))
		{
			return "";
		}
		return File.ReadAllText(path);
	}

	private void EnsureProjectRoot()
	{
		if (string.IsNullOrWhiteSpace(ProjectRoot) || !Directory.Exists(ProjectRoot))
		{
			throw new InvalidOperationException("Unity project root was not found.");
		}
	}

	private static string ExtractGuid(string metaText)
	{
		Match match = Regex.Match(metaText, "guid:\\s*([a-fA-F0-9]+)");
		if (!match.Success)
		{
			return "";
		}
		return match.Groups[1].Value;
	}

	private static string NormalizeResourcePath(string path, string fallback)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return fallback;
		}
		string text = path.Trim().Replace("\\", "/");
		int num = text.IndexOf("Resources/", StringComparison.OrdinalIgnoreCase);
		if (num >= 0)
		{
			string text2 = text;
			int num2 = num + "Resources/".Length;
			text = text2.Substring(num2, text2.Length - num2);
		}
		if (text.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			int num2 = ".json".Length;
			text = text2.Substring(0, text2.Length - num2);
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return fallback;
	}

	private static bool StringEqualsIgnoringLineEndings(string a, string b)
	{
		return NormalizeLineEndings(a) == NormalizeLineEndings(b);
	}

	private static string NormalizeLineEndings(string value)
	{
		return value.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
	}
}


