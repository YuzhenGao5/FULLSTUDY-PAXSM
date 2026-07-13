using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PAXSMStudyAgent;

internal sealed class AgentEngine
{
	private sealed class LlmDecision
	{
		public bool NeedsClarification { get; set; }

		public List<string> Questions { get; set; } = new List<string>();

		public JsonElement? Config { get; set; }

		public List<string> Warnings { get; set; } = new List<string>();

		public string Summary { get; set; } = "";
	}

	public const string ProviderOllama = "Ollama";

	public const string ProviderOpenAiCompatible = "OpenAI-compatible";

	public const string DefaultOllamaBaseUrl = "http://localhost:11434";

	public const string DefaultOllamaModel = "qwen3:14b";

	public const string DefaultOpenAiModel = "gpt-4.1-mini";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	public async Task<AgentResult> GenerateAsync(string userRequest, string provider, string? connectionValue, string? model, CancellationToken cancellationToken)
	{
		string text = (string.IsNullOrWhiteSpace(provider) ? "Ollama" : provider.Trim());
		if (text.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
		{
			return await GenerateWithOllamaAsync(userRequest, connectionValue, model, cancellationToken);
		}
		if (text.Equals("OpenAI-compatible", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(connectionValue))
			{
				throw new InvalidOperationException("LLM connection is required. Enter an OpenAI API key before generating a study preview.");
			}
			return await GenerateWithOpenAiCompatibleApiAsync(userRequest, connectionValue.Trim(), model, cancellationToken);
		}
		throw new InvalidOperationException("Unsupported LLM provider: " + text);
	}

	public AgentResult GenerateWithLocalRules(string userRequest)
	{
		string text = userRequest ?? "";
		string text2 = text.ToLowerInvariant();
		List<string> list = new List<string>();
		bool allowDefaults = AllowsAgentDefaults(text2);
		string text3 = DetectDesign(text2);
		string text4 = DetectConstruct(text2);
		int num = DetectScale(text2);
		string text5 = DetectResponseMode(text2);
		string responseMode = ResolveResponseMode(num, text5);
		int num2 = ((num > 0) ? num : DefaultScaleForConstruct(text4));
		if (num2 == 21)
		{
			responseMode = "slider";
		}
		int num3 = DetectNumber(text2, "(?:participant|p|被试|参与者)\\s*0*(\\d+)", 1);
		int sessionNumber = DetectNumber(text2, "(?:session|s|阶段|场次)\\s*0*(\\d+)", 1);
		bool flag = ContainsAny(text2, "random", "随机");
		List<string> list2 = DetectConditions(text2);
		string counterbalancingOrder = (ContainsAny(text2, "latin", "拉丁") ? "LatinSquare" : ((list2.Count > 0) ? string.Join("/", list2) : "A"));
		string constructLabel = text4 switch
		{
			"cognitive_load" => "CognitiveLoad", 
			"usability" => "Usability", 
			"physical_demand" => "PhysicalDemand", 
			"safety_comfort" => "SafetyComfort", 
			"continuance_intention" => "ContinuanceIntention", 
			"presence" => "Presence", 
			"trust" => "Trust", 
			"stress" => "Stress", 
			_ => "CustomConstruct", 
		};
		List<string> list3 = BuildClarifyingQuestions(text2, text4, text3, num, text5, allowDefaults);
		if (list3.Count > 0)
		{
			return new AgentResult
			{
				Source = "Local rules",
				Summary = "I understood part of the study request, but need a few details before writing the Unity config or question bank.",
				NeedsClarification = true,
				Questions = list3,
				Warnings = list
			};
		}
		if (text3 == "within-subjects" && !ContainsAny(text2, "condition", "conditions", "条件", "a/b", "ab"))
		{
			list.Add("Detected a within-subjects design, but no concrete condition names were provided. Add condition labels before the formal study.");
		}
		if (flag)
		{
			list.Add("Question randomization is recorded in the config, but the current Unity runtime does not yet reorder questions automatically.");
		}
		if (text4 == "usability" && num > 0 && num != 5)
		{
			list.Add("Usability was detected. SUS is normally administered on a 5-point scale; the agent kept your requested scale, so document this adaptation if used formally.");
		}
		if (num2 == 21 && string.Equals(text5, "card", StringComparison.OrdinalIgnoreCase))
		{
			list.Add("21-point scales are locked to slider mode. The requested card mode was changed to slider.");
		}
		StudyConfig studyConfig = new StudyConfig
		{
			studyName = ((text4 == "cognitive_load") ? "PAXSM Cognitive Load Full Study" : "PAXSM Full Study"),
			design = text3,
			construct = text4,
			participantId = $"P{num3:000}",
			participantNumber = num3,
			sessionNumber = sessionNumber,
			conditionLabel = BuildConditionLabel(constructLabel, text3, list2),
			conditions = list2,
			counterbalancingOrder = counterbalancingOrder,
			randomizeQuestions = flag,
			questionBankResourcesPath = "QuestionBanks/Scale",
			responseMode = responseMode,
			scale = num2,
			outputFolder = "C:/PAXSM_FullStudy_Data",
			outputSubfolder = "ExportsCSV",
			fileNamePrefix = "PAXSM",
			exportMergedCsv = true,
			exportRawStageEvents = true,
			exportOnQuit = true,
			preventOverwrite = true,
			naturalLanguageRequest = text,
			generatedSummary = BuildSummary(text3, text4, responseMode, num2)
		};
		return new AgentResult
		{
			Config = studyConfig,
			Source = "Local rules",
			Summary = studyConfig.generatedSummary,
			Warnings = list
		};
	}

	public string BuildInstantDraft(string userRequest)
	{
		AgentResult agentResult = TryBuildContextRecommendation(userRequest, "Local draft");
		if (agentResult == null)
		{
			return "我正在先理解你的研究目标。正式配置还在生成中，在你点击 Confirm and apply 之前不会写入 Unity 文件。";
		}
		object obj = ((agentResult.TaskConfigs.Count > 0) ? ((object)agentResult.TaskConfigs) : ((object)new List<StudyConfig> { agentResult.Config }));
		List<string> list = new List<string>();
		foreach (StudyConfig item in (List<StudyConfig>)obj)
		{
			if (!string.IsNullOrWhiteSpace(item.construct))
			{
				list.Add(QuestionnaireTaskLabel(item));
			}
		}
		string text = ((list.Count == 0) ? "我先给一个初步判断：这更像是在评估系统本身，需要先确认问卷目标。" : ("我先给一个初步判断：可能会推荐 " + JoinHumanReadable(list) + "。"));
		if (list.Count > 0 && IsDirectQuestionnaireConfigurationRequest(userRequest))
		{
			text = "I will configure the requested questionnaire(s): " + JoinHumanReadable(list) + ".";
		}
		if (ContainsResponseDetectionContext(userRequest) || ContainsQuestionnaireSystemContext(userRequest))
		{
			text += " 作答检测本身建议用完成率、检测准确率、漏检率和延迟等客观日志验证。";
		}
		if (ContainsPerformanceContext(userRequest) || ContainsTaskVariationContext(userRequest))
		{
			text += " 任务表现建议用 Unity 侧客观指标记录。";
		}
		return text + " 我还在让语言模型检查并整理正式 preview。";
	}

	private async Task<AgentResult> GenerateWithOpenAiCompatibleApiAsync(string userRequest, string apiKey, string? model, CancellationToken cancellationToken)
	{
		string model2 = (string.IsNullOrWhiteSpace(model) ? "gpt-4.1-mini" : model.Trim());
		using HttpClient client = new HttpClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
		string content = JsonSerializer.Serialize(new
		{
			model = model2,
			temperature = 0.1,
			response_format = new
			{
				type = "json_object"
			},
			messages = new object[2]
			{
				new
				{
					role = "system",
					content = BuildSystemPrompt()
				},
				new
				{
					role = "user",
					content = BuildModelUserMessage(userRequest)
				}
			}
		});
		using HttpResponseMessage response = await client.PostAsync("https://api.openai.com/v1/chat/completions", new StringContent(content, Encoding.UTF8, "application/json"), cancellationToken);
		string text = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"HTTP {response.StatusCode}: {text}");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text);
		string? text2 = jsonDocument.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
		if (string.IsNullOrWhiteSpace(text2))
		{
			throw new InvalidOperationException("The LLM returned an empty response.");
		}
		return BuildAgentResultFromLlmContent(text2, userRequest, "OpenAI-compatible");
	}

	private async Task<AgentResult> GenerateWithOllamaAsync(string userRequest, string? baseUrl, string? model, CancellationToken cancellationToken)
	{
		string resolvedBaseUrl = NormalizeOllamaBaseUrl(baseUrl);
		string resolvedModel = (string.IsNullOrWhiteSpace(model) ? "qwen3:14b" : model.Trim());
		using HttpClient client = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5L)
		};
		string content = JsonSerializer.Serialize(new
		{
			model = resolvedModel,
			stream = false,
			format = "json",
			think = false,
			keep_alive = "1h",
			options = new
			{
				temperature = 0.0,
				top_p = 0.9,
				num_ctx = 4096,
				num_predict = 900
			},
			messages = new object[2]
			{
				new
				{
					role = "system",
					content = BuildOllamaSystemPrompt()
				},
				new
				{
					role = "user",
					content = BuildModelUserMessage(userRequest)
				}
			}
		});
		try
		{
			using HttpResponseMessage response = await client.PostAsync(resolvedBaseUrl + "/api/chat", new StringContent(content, Encoding.UTF8, "application/json"), cancellationToken);
			string text = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"Ollama request failed at {resolvedBaseUrl}. HTTP {response.StatusCode}: {text}");
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			string? text2 = jsonDocument.RootElement.GetProperty("message").GetProperty("content").GetString();
			if (string.IsNullOrWhiteSpace(text2))
			{
				throw new InvalidOperationException("Ollama returned an empty response.");
			}
			return BuildAgentResultFromLlmContent(text2, userRequest, "Ollama local");
		}
		catch (HttpRequestException innerException)
		{
			throw new InvalidOperationException($"Could not reach Ollama at {resolvedBaseUrl}. Start Ollama and install/select the model '{resolvedModel}'.", innerException);
		}
		catch (TaskCanceledException innerException2) when (!cancellationToken.IsCancellationRequested)
		{
			throw new InvalidOperationException("Ollama did not respond within 5 minutes at " + resolvedBaseUrl + ". Try a smaller local model or confirm the model has finished loading.", innerException2);
		}
	}

	private static string NormalizeOllamaBaseUrl(string? baseUrl)
	{
		return (string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.Trim()).TrimEnd('/');
	}

	private static string BuildSystemPrompt()
	{
		return "You are a practical study-setup advisor for a Unity XR questionnaire system named PAXSM. Your job is to understand the researcher's natural-language request, recommend academically careful questionnaire settings when asked, and then produce the config JSON. Return only one JSON object with this schema: {needsClarification:boolean, questions:string[], warnings:string[], summary:string, config:object|null}. Only ask clarification about researcher-facing questionnaire decisions: questionnaire/construct, Likert or rating scale points, and response mode. Do not ask about experimental conditions, condition labels, counterbalancing, within-subjects condition names, or between-subjects groups unless the researcher explicitly asks to configure full experimental conditions. This agent is currently setting up the questionnaire only. Never ask the researcher for internal implementation fields such as schemaVersion, studyName, studyVersion, participantId, participantNumber, sessionNumber, experimenterId, conditionLabel, questionBankResourcesPath, outputFolder, outputSubfolder, fileNamePrefix, or resource paths. Fill those automatically. If the user says suggest, recommend, you decide, use defaults, 你建议, 你决定, 帮我推荐, or similar wording, treat that as permission to choose sensible defaults instead of asking follow-up questions. If the user asks which questionnaire, which scale, what should be used, or says 'should I use', treat that as a request for recommendation. Do not ask them to name a construct first. Do not list the entire questionnaire catalog. Recommend only the best 1-3 questionnaire tasks for the current request unless the researcher explicitly asks for all available options. For a broad request like testing how a system is or what questionnaire an experiment needs, recommend a core battery plus conditional modules. Core normally includes usability and general user experience. Add presence only when XR, VR, immersive, virtual environment, or presence is mentioned. Mention workload, cybersickness, embodiment/agency, social presence/trust, and learning/rehab measures as optional modules only when the study context justifies them. Prefer existing validated or widely used questionnaires. Recommend a custom questionnaire only when the researcher explicitly asks for a custom/self-made questionnaire, or when no suitable existing questionnaire family fits the construct. Distinguish standardized, adapted, and custom instruments. If PAXSM changes wording, response options, or scoring, mark the configuration as adapted, not custom, when it is still based on an existing questionnaire family. Never imply that an adapted PAXSM question bank is the fully validated original questionnaire. Avoid vague terms such as SUS-like, VRSUQ-like, presence-like, or multimodal-presence-like in the final summary. If the context is hard/easy tasks, difficult/simple questions, task difficulty, mental effort, perceived demand, or how users feel about solving problems, recommend cognitive_load. The standardized options are NASA-TLX or SIM-TLX; PAXSM should treat any changed implementation as adapted. If the context is letting people try a system and judge whether it is good to use, easy to use, usable, smooth, intuitive, or user-friendly, recommend usability. Standardized options include SUS or UMUX-Lite; PAXSM should treat changed wording, response format, or scoring as adapted. If the context is a questionnaire system, survey system, response-detection system, or a system that detects whether users answered, recommend usability for the system evaluation, and explain that answer detection itself should be evaluated with objective logs such as completion rate, detection accuracy, false positives, false negatives, missing-response rate, and detection latency. Do not ask the researcher to name a psychological construct first. If the context is XR/VR immersion, feeling inside the virtual environment, presence, spatial presence, or sense of being there, recommend presence using a 7-point card questionnaire. If the context is a brand-new system, new prototype, first evaluation, or newly built XR application, also recommend usability as a baseline even if the researcher did not explicitly say 'easy to use'. If the context includes multiple tasks, different tasks, task types, or users doing tasks, also recommend cognitive_load to capture perceived task demand, unless the researcher explicitly says they only want immersion. If the context is rehabilitation, physical training, upper-limb movement, body burden, perceived exertion, fatigue, or physical demand, recommend physical_demand rather than embodiment. Standardized options include Borg RPE, NASA-TLX Physical Demand, fatigue scales, or domain-specific exertion measures. If the context is rehabilitation safety, safety feeling, comfort, pain, stability, risk, or fear of injury, recommend safety_comfort rather than trust. Use validated rehabilitation safety, pain, fatigue, or comfort measures when available for the target population. If the context is willingness to continue, adoption, adherence, acceptance, intention to use, or future use, recommend continuance_intention. TAM/UTAUT-style intention-to-use items or rehabilitation adherence/acceptance measures are standardized options. If the context includes an HMD, VR motion, nausea, dizziness, cybersickness, eye strain, or long immersive exposure, recommend motion_sickness. Standardized options include FMS for a quick check and SSQ or VRSQ for fuller symptom measurement. If the context includes avatar, virtual hands, body tracking, body ownership, agency, or embodiment, recommend presence_self or embodiment and name validated embodiment/agency scales as options. If the context includes virtual humans, collaborators, audiences, multi-user interaction, or an agent/coach relationship, recommend social presence, trust, or virtual therapist alliance as appropriate. If the context is trust, reliability, confidence in system decisions, predictability, or dependable automation, recommend trust using a 7-point card questionnaire. Do not map general safety or rehabilitation comfort to trust. If the context is stress, pressure, anxiety, relaxation, or tension, recommend stress using a 7-point card questionnaire. If the researcher mentions task performance, accuracy, completion, score, breathing-training outcome, or performance under different difficulty levels, explain that objective performance is best logged as task metrics rather than configured as a questionnaire. If the researcher mentions multiple measurement goals, recommend separate questionnaire tasks for each goal instead of collapsing everything into one questionnaire. Use this expanded XR questionnaire catalog when recommending questionnaires: usability can use SUS or UMUX-Lite as standardized options; general user experience can use UEQ-S/UEQ, AttrakDiff, or a clearly labeled custom brief XR feedback bank; presence can use IPQ, Presence Questionnaire, ITC-SOPI, or SUS-PQ; workload can use NASA-TLX or SIM-TLX; physical demand can use Borg RPE, NASA-TLX Physical Demand, fatigue, exertion, or pain scales; safety/comfort can use rehabilitation safety, pain, fatigue, comfort, or fear-of-injury measures; continuance intention can use TAM/UTAUT-style intention-to-use or adherence/acceptance measures; cybersickness can use FMS, SSQ, or VRSQ; embodiment/agency can use VEQ or body-ownership/agency scales; social presence can use Networked Minds or co-presence/social-presence scales. Use these recommendation defaults unless the user states otherwise: cognitive_load uses 21-point slider; usability normally uses 5-point card; presence variants, user_experience, physical_demand, safety_comfort, continuance_intention, simulator_realism, immersion, embodiment, virtual_therapist_alliance, trust, stress, and custom constructs use 7-point card; motion_sickness uses 4-point card symptom severity. A 21-point scale must always use slider mode; never return card for a 21-point scale. If the user asked for card with 21 points, set slider and add a warning. For questionnaire-only setup, set conditions=[] and conditionLabel=QuestionnaireOnly unless the researcher explicitly provides a current run label. If a researcher-facing choice is missing and the user has not asked for a recommendation/default, set needsClarification=true, ask 1-3 short questions, and set config=null. When information is sufficient, set needsClarification=false and fill config with: {schemaVersion,studyName,studyVersion,design,construct,participantId,participantNumber,sessionNumber,experimenterId,conditionLabel,conditions,counterbalancingOrder,randomizeQuestions,questionBankResourcesPath,responseMode,scale,recommendationRole,instrumentName,instrumentStatus,recommendedStandardInstrument,recommendationRationale,outputFolder,outputSubfolder,fileNamePrefix,exportMergedCsv,exportRawStageEvents,exportOnQuit,preventOverwrite,naturalLanguageRequest,generatedSummary}. Use exactly those config property names. Do not invent aliases such as studyDesign, conditionLabels, questionnaireType, answerMode, or scalePoints. The config.scale value must be an integer only, such as 5, 7, or 21. Never use an object, nested JSON, or text for scale. For config metadata, recommendationRole should be Core or Conditional module: reason; instrumentName should name the primary existing questionnaire or adapted questionnaire family; instrumentStatus must be one of standardized_available, standardized_recommended_not_deployed, adapted_short_form, custom_generated, researcher_supplied_custom, researcher_supplied_standardized_candidate, or unknown_needs_review; recommendedStandardInstrument should name a validated option when relevant; recommendationRationale should explain why it fits this study. Use custom_generated only for explicit self-made questionnaires or no suitable existing questionnaire. For the current questionnaire-only workflow, use design=questionnaire-only. Use construct values like cognitive_load, usability, user_experience, physical_demand, safety_comfort, continuance_intention, presence, presence_spatial, presence_social, presence_self, presence_combined, motion_sickness, simulator_realism, immersion, embodiment, virtual_therapist_alliance, trust, stress, custom. config.construct must contain exactly one construct value, never a comma-separated list. If multiple questionnaires are needed, explain them in the summary; the app will manage separate tasks. If the researcher changes their mind in later clarifications, the latest clarification overrides earlier values. Use these exact internal defaults: schemaVersion=paxsm-study-config-v1, studyVersion=1.0, participantId=P001, participantNumber=1, sessionNumber=1, questionBankResourcesPath=QuestionBanks/Scale, outputFolder=C:/PAXSM_FullStudy_Data, outputSubfolder=ExportsCSV, fileNamePrefix=PAXSM, exportMergedCsv=true, exportRawStageEvents=true, exportOnQuit=true, preventOverwrite=true. The summary should sound like a natural recommendation, not a form checklist. Never include markdown.";
	}

	private static string BuildOllamaSystemPrompt()
	{
		return "You configure questionnaire-only studies for PAXSM, a Unity XR questionnaire system. Return only one JSON object: {needsClarification:boolean, questions:string[], warnings:string[], summary:string, config:object|null}. Do not use markdown. Do not ask for internal fields, study name, paths, conditions, counterbalancing, participant IDs, or output folders. If the researcher asks for advice or gives enough context, recommend sensible defaults instead of asking them to name a construct. Do not list the full questionnaire catalog. Recommend only the best 1-3 questionnaires for the current request unless the researcher explicitly asks for all available options. For a broad request like testing how a system is or what questionnaire an experiment needs, recommend a core battery plus conditional modules. Core normally includes usability and general user experience. Add presence only when XR, VR, immersive, virtual environment, or presence is mentioned. Mention workload, cybersickness, embodiment/agency, social presence/trust, and learning/rehab measures as optional modules only when justified by context. Prefer existing validated or widely used questionnaires. Recommend a custom questionnaire only when the researcher explicitly asks for a custom/self-made questionnaire, or when no suitable existing questionnaire family fits the construct. If PAXSM changes wording, response options, or scoring, mark the configuration as adapted, not custom, when it is still based on an existing questionnaire family. Avoid vague final-summary labels such as SUS-like, VRSUQ-like, presence-like, or multimodal-presence-like. Use design=questionnaire-only, conditions=[], conditionLabel=QuestionnaireOnly, counterbalancingOrder=None, randomizeQuestions=false. Default config fields are schemaVersion=paxsm-study-config-v1, studyVersion=1.0, participantId=P001, participantNumber=1, sessionNumber=1, questionBankResourcesPath=QuestionBanks/Scale, outputFolder=C:/PAXSM_FullStudy_Data, outputSubfolder=ExportsCSV, fileNamePrefix=PAXSM, exportMergedCsv=true, exportRawStageEvents=true, exportOnQuit=true, preventOverwrite=true. Use these constructs and defaults: cognitive_load 21-point slider for task difficulty/workload, with NASA-TLX or SIM-TLX as standardized options; usability 5-point card for system usability, with SUS or UMUX-Lite as standardized options; user_experience 7-point card for broad XR experience, usually as a custom brief XR feedback bank unless a validated UEQ/UEQ-S-style option is selected; physical_demand 7-point card for rehabilitation body burden/perceived exertion, with Borg RPE, NASA-TLX Physical Demand, fatigue, exertion, or pain scales as standardized options; safety_comfort 7-point card for rehabilitation safety, comfort, pain, stability, and risk; continuance_intention 7-point card for willingness to continue, adherence, acceptance, and future use; presence_spatial 7-point card for being-there/spatial presence; presence_social 7-point card for virtual humans or multi-user interaction; presence_self 7-point card only for avatar/body ownership/agency; presence_combined 7-point card for general presence/immersion; motion_sickness 4-point card for nausea/dizziness/eye strain/cybersickness, with FMS/SSQ/VRSQ as standardized options; simulator_realism 7-point card for simulation or training realism; immersion 7-point card for absorption/loss of time; virtual_therapist_alliance 7-point card for virtual therapist/coach; trust 7-point card only for reliability, automation, or system decision confidence; stress 7-point card. config.construct must contain exactly one construct value, never a comma-separated list. A 21-point scale must use slider. Other scales use card unless the researcher says otherwise. Performance, answer detection, completion rate, false positives, false negatives, and latency are objective logs, not questionnaire constructs; mention this in summary or warnings when relevant. Use instrumentStatus values from this set only: standardized_available, standardized_recommended_not_deployed, adapted_short_form, custom_generated, researcher_supplied_custom, researcher_supplied_standardized_candidate, unknown_needs_review. If genuinely missing, ask only 1-3 short questions about questionnaire construct, scale points, or response mode. When config is not null, use exactly these property names: schemaVersion,studyName,studyVersion,design,construct,participantId,participantNumber,sessionNumber,experimenterId,conditionLabel,conditions,counterbalancingOrder,randomizeQuestions,questionBankResourcesPath,responseMode,scale,recommendationRole,instrumentName,instrumentStatus,recommendedStandardInstrument,recommendationRationale,outputFolder,outputSubfolder,fileNamePrefix,exportMergedCsv,exportRawStageEvents,exportOnQuit,preventOverwrite,naturalLanguageRequest,generatedSummary.";
	}

	private static string BuildModelUserMessage(string userRequest)
	{
		bool num = WantsQuestionnaireRecommendation(userRequest);
		bool flag = ContainsTaskDifficultyQuestionnaireContext(userRequest);
		bool flag2 = ContainsUsabilityQuestionnaireContext(userRequest);
		bool flag3 = ContainsPresenceQuestionnaireContext(userRequest);
		bool flag4 = ContainsTrustQuestionnaireContext(userRequest);
		bool flag5 = ContainsStressQuestionnaireContext(userRequest);
		bool flag6 = ContainsPhysicalDemandQuestionnaireContext(userRequest);
		bool flag7 = ContainsSafetyComfortQuestionnaireContext(userRequest);
		bool flag8 = ContainsContinuanceIntentionQuestionnaireContext(userRequest);
		bool flag9 = ContainsPerformanceContext(userRequest);
		bool flag10 = ContainsNewSystemEvaluationContext(userRequest);
		bool flag11 = ContainsTaskVariationContext(userRequest);
		bool flag12 = ContainsUserExperienceQuestionnaireContext(userRequest);
		bool flag13 = ContainsMotionSicknessQuestionnaireContext(userRequest);
		bool flag14 = ContainsSpatialPresenceQuestionnaireContext(userRequest);
		bool flag15 = ContainsSocialPresenceQuestionnaireContext(userRequest);
		bool flag16 = ContainsSelfPresenceQuestionnaireContext(userRequest);
		bool flag17 = ContainsSimulatorRealismQuestionnaireContext(userRequest);
		bool flag18 = ContainsVirtualTherapistAllianceQuestionnaireContext(userRequest);
		bool flag19 = ContainsQuestionnaireSystemContext(userRequest);
		bool flag20 = ContainsResponseDetectionContext(userRequest);
		bool flag21 = ShouldUseBroadSystemEvaluationDefaults(userRequest);
		if (!num && !flag && !flag2 && !flag3 && !flag4 && !flag5 && !flag6 && !flag7 && !flag8 && !flag9 && !flag10 && !flag11 && !flag12 && !flag13 && !flag14 && !flag15 && !flag16 && !flag17 && !flag18 && !flag19 && !flag20 && !flag21)
		{
			return userRequest;
		}
		StringBuilder stringBuilder = new StringBuilder(userRequest);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Important: the latest researcher message is asking for your questionnaire recommendation or gives enough task context to recommend one.");
		stringBuilder.AppendLine("Choose sensible defaults for missing researcher-facing settings instead of asking the researcher to name the construct first.");
		stringBuilder.AppendLine("Prefer existing validated or widely used questionnaires. Use custom/self-made items only if the researcher explicitly asks for a custom questionnaire or no suitable existing questionnaire family fits the construct. If PAXSM adapts wording, response format, or scoring, label it as adapted.");
		if (ResearcherRejectedCustomInstrument(userRequest))
		{
			stringBuilder.AppendLine("The researcher explicitly rejected custom/self-made questionnaires. Do not label known questionnaire families as custom; use existing standardized instruments or adapted implementations of existing instruments.");
		}
		stringBuilder.AppendLine("Do not list the entire questionnaire catalog. Recommend only the best 1-3 questionnaire tasks for this request.");
		if (flag21)
		{
			stringBuilder.AppendLine("Domain hint: this is a broad system-evaluation request. Recommend a core battery first: usability plus general user experience. Add presence only if XR, VR, immersive, virtual environment, or presence is explicitly mentioned. Mention workload, cybersickness, embodiment/agency, social presence/trust, and learning/rehab measures as conditional modules only when the study context justifies them. Distinguish standardized options from the generated PAXSM bank.");
		}
		if (flag)
		{
			stringBuilder.AppendLine("Domain hint: hard/easy problems, perceived question difficulty, and feelings while solving questions should normally be configured as cognitive_load. NASA-TLX or SIM-TLX are standardized options; changed PAXSM wording/response/scoring should be labeled adapted.");
		}
		if (flag2)
		{
			stringBuilder.AppendLine("Domain hint: asking people to try a system and say whether it is good or easy to use should normally be configured as usability. SUS or UMUX-Lite are standardized options; changed PAXSM wording/response/scoring should be labeled adapted.");
		}
		if (flag19 || flag20)
		{
			stringBuilder.AppendLine("Domain hint: a questionnaire or response-detection system should be evaluated with a usability questionnaire. Whether users answered is an objective detection metric, not a psychological questionnaire construct.");
		}
		if (flag3)
		{
			stringBuilder.AppendLine("Domain hint: XR/VR immersion, presence, or feeling inside the virtual world should normally be configured as presence/immersion with a 7-point card questionnaire.");
		}
		if (flag14)
		{
			stringBuilder.AppendLine("Domain hint: being there, place illusion, or spatial presence should use a spatial-presence module. IPQ or Presence Questionnaire spatial items are standardized options.");
		}
		if (flag15)
		{
			stringBuilder.AppendLine("Domain hint: VR interaction with other people, virtual humans, collaborators, or audiences should use social/co-presence items.");
		}
		if (flag16)
		{
			stringBuilder.AppendLine("Domain hint: avatars, virtual bodies, hands, limbs, body ownership, or agency should use self-presence / embodiment items.");
		}
		if (flag12)
		{
			stringBuilder.AppendLine("Domain hint: broad XR user experience can use a custom brief XR feedback bank, or standardized UEQ-S/UEQ/AttrakDiff-style instruments when comparability is needed.");
		}
		if (flag6)
		{
			stringBuilder.AppendLine("Domain hint: rehabilitation movement, upper-limb training, physical burden, fatigue, or perceived exertion should use a physical_demand module. Do not map this to embodiment unless the researcher mentions avatar, virtual hands, body ownership, or agency.");
		}
		if (flag7)
		{
			stringBuilder.AppendLine("Domain hint: safety feeling, comfort, pain, stability, injury risk, or rehabilitation safety should use a safety_comfort module. Do not map this to trust unless the researcher asks about reliability or confidence in automated system decisions.");
		}
		if (flag8)
		{
			stringBuilder.AppendLine("Domain hint: willingness to continue, future use, acceptance, adherence, adoption, or intention to use should use a continuance_intention module. This can be separate from general UX if the researcher explicitly asks for it.");
		}
		if (flag13)
		{
			stringBuilder.AppendLine("Domain hint: nausea, dizziness, eye strain, HMD discomfort, cybersickness, or VR tolerability should use motion_sickness with 4-point symptom severity.");
		}
		if (flag17)
		{
			stringBuilder.AppendLine("Domain hint: simulator realism, training realism, haptic realism, or visual realism should use simulator_realism items.");
		}
		if (flag18)
		{
			stringBuilder.AppendLine("Domain hint: a virtual therapist, coach, guide, or therapeutic agent should use virtual therapist alliance items.");
		}
		if (flag10)
		{
			stringBuilder.AppendLine("Domain hint: a brand-new system or prototype evaluation should include a usability baseline, usually 5-point card in PAXSM, even when the researcher does not explicitly say 'usability'.");
		}
		if (flag11)
		{
			stringBuilder.AppendLine("Domain hint: multiple or different task types usually justify cognitive_load, and objective task metrics should be logged separately.");
		}
		if (flag4)
		{
			stringBuilder.AppendLine("Domain hint: trust, reliability, confidence in system decisions, or predictable automation should normally be configured as trust with a 7-point card questionnaire.");
		}
		if (flag5)
		{
			stringBuilder.AppendLine("Domain hint: stress, pressure, anxiety, relaxation, or tension should normally be configured as stress with a 7-point card questionnaire.");
		}
		if (flag9)
		{
			stringBuilder.AppendLine("Domain hint: objective task performance should be logged as task metrics. Do not treat performance itself as a questionnaire; recommend relevant questionnaires only for subjective constructs.");
		}
		if (CountDetectedQuestionnaireContexts(userRequest) > 1)
		{
			stringBuilder.AppendLine("Domain hint: this request contains multiple measurement goals. Recommend separate questionnaire tasks and make the summary mention all of them.");
		}
		stringBuilder.AppendLine("Do not ask for studyName, studyVersion, questionBankResourcesPath, resource paths, conditions, counterbalancing, or other internal config fields.");
		return stringBuilder.ToString();
	}

	private static bool ContainsRecommendationIntent(string text)
	{
		return ContainsAny(text, "suggest", "recommend", "you decide", "use defaults", "default", "choose for me", "你建议", "建议一下", "帮我推荐", "你决定", "你来定", "默认", "推荐一下");
	}

	private static bool ContainsQuestionnaireRecommendationIntent(string text)
	{
		return ContainsAny(text, "which questionnaire", "what questionnaire", "which scale", "what scale", "what should i use", "should i use", "questionnaire should i use", "应该用什么问卷", "用什么问卷", "什么问卷", "应该用什么量表", "用什么量表", "什么量表", "该用", "应该", "推荐什么");
	}

	private static bool WantsQuestionnaireRecommendation(string text)
	{
		if (!ContainsRecommendationIntent(text) && !ContainsQuestionnaireRecommendationIntent(text))
		{
			return ContainsAny(text, "推荐", "推荐一下", "建议", "建议一下", "你建议", "你决定", "你来定", "帮我推荐", "默认", "什么样的问卷", "需要什么样的问卷", "什么问卷", "什么量表");
		}
		return true;
	}

	private static bool IsDirectQuestionnaireConfigurationRequest(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		bool flag = ContainsAny(text2, "configure", "set up", "setup", "add", "include", "use ", "apply", "配置", "设置", "加入", "添加", "加", "用", "采用", "帮我配置");
		bool flag2 = ContainsAny(text2, "ssq", "simulator sickness questionnaire", "sus", "system usability scale", "nasa-tlx", "nasa tlx", "tlx", "fms", "vrsq", "ueq", "umux", "ipq", "borg", "rpe");
		return flag && flag2;
	}

	private static bool ContainsExactSsqRequest(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (ContainsAny(text2, "ssq", "simulator sickness questionnaire"))
		{
			return IsDirectQuestionnaireConfigurationRequest(text2);
		}
		return false;
	}

	private static bool ContainsExactSusRequest(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (ContainsAny(text2, "sus", "system usability scale"))
		{
			return IsDirectQuestionnaireConfigurationRequest(text2);
		}
		return false;
	}

	private static bool ContainsExactNasaTlxRequest(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (ContainsAny(text2, "nasa-tlx", "nasa tlx", "nasa_tlx", "tlx"))
		{
			return IsDirectQuestionnaireConfigurationRequest(text2);
		}
		return false;
	}

	private static bool ContainsExactUeqSRequest(string? text)
	{
		return ContainsAny((text ?? "").ToLowerInvariant(), "ueq-s", "ueq s", "ueq_short", "ueq short", "user experience questionnaire - short", "user experience questionnaire short");
	}

	private static bool ContainsSingleItemWorkloadRequest(string? text)
	{
		return ContainsAny((text ?? "").ToLowerInvariant(), "single item", "single-item", "one item", "only one item", "one question", "how mentally demanding was the task", "单题", "一个题", "只问一个", "只想问一个", "不想用完整 nasa", "不想用完整nasa", "不要完整 nasa");
	}

	private static bool ContainsResearcherSuppliedCustomItems(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (!Regex.IsMatch(text2, "\\u8fd9\\s*(?:\\d+|[一二三四五六七八九十]+)\\s*\\u4e2a?\\s*\\u9898", RegexOptions.IgnoreCase) && !Regex.IsMatch(text2, "\\u9898\\s*[:\\uff1a]", RegexOptions.IgnoreCase))
		{
			return ContainsAny(text2, "my items", "these items", "these questions", "researcher-supplied", "i want to use these", "i have these", "这 8 个题", "这8个题", "这些题", "我想用这", "我自带题项", "自带题项");
		}
		return true;
	}

	private static bool ContainsResearcherImportedStandardQuestionnaire(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (ContainsAny(text2, "sus", "system usability scale"))
		{
			if (!Regex.IsMatch(text2, "\\u5df2\\u7ecf.*\\u5bfc\\u5165", RegexOptions.IgnoreCase) && !Regex.IsMatch(text2, "\\u81ea\\u5df1.*\\u5bfc\\u5165", RegexOptions.IgnoreCase))
			{
				return ContainsAny(text2, "already imported", "imported full", "complete sus", "full sus", "original items", "已经导入", "自己导入", "完整的 sus", "完整 sus", "原题", "原始题");
			}
			return true;
		}
		return false;
	}

	private static bool ShouldUseBroadSystemEvaluationDefaults(string text)
	{
		if (!WantsQuestionnaireRecommendation(text))
		{
			return false;
		}
		if (ContainsTaskDifficultyQuestionnaireContext(text) || ContainsMotionSicknessQuestionnaireContext(text) || ContainsSpatialPresenceQuestionnaireContext(text) || ContainsSocialPresenceQuestionnaireContext(text) || ContainsSelfPresenceQuestionnaireContext(text) || ContainsSimulatorRealismQuestionnaireContext(text) || ContainsVirtualTherapistAllianceQuestionnaireContext(text) || ContainsPhysicalDemandQuestionnaireContext(text) || ContainsSafetyComfortQuestionnaireContext(text) || ContainsContinuanceIntentionQuestionnaireContext(text) || ContainsTrustQuestionnaireContext(text) || ContainsStressQuestionnaireContext(text) || ContainsQuestionnaireSystemContext(text) || ContainsResponseDetectionContext(text))
		{
			return false;
		}
		return ContainsAny(text, "system", "study", "experiment", "evaluation", "evaluate", "test the system", "test system", "which questionnaire", "what questionnaire", "what kind of questionnaire", "系统", "这个系统", "测试系统", "测试", "实验", "这个实验", "评估", "问卷", "什么样的问卷", "什么问卷", "系统怎么样");
	}

	private static bool ContainsXrOrImmersiveSystemContext(string text)
	{
		return ContainsAny(text, "xr", "vr", "virtual reality", "virtual environment", "virtual world", "immersive", "immersion", "presence", "unity xr", "mixed reality", "augmented reality", "沉浸", "沉浸式", "临场", "虚拟环境", "虚拟世界", "虚拟现实", "xr世界", "vr世界", "混合现实", "增强现实");
	}

	private static bool ContainsTaskDifficultyQuestionnaireContext(string text)
	{
		return ContainsAny(text, "hard question", "hard questions", "easy question", "easy questions", "difficult question", "simple question", "hard task", "easy task", "task difficulty", "problem difficulty", "mental effort", "mental demand", "cognitive load", "workload", "cognitive workload", "nasa", "nasa-tlx", "tlx", "perceived difficulty", "feel about the question", "feelings about questions", "难题", "简单题", "题目难度", "对题的感觉", "做题的感觉", "做题时的感觉", "任务难度", "主观难度", "认知负荷", "心理负荷", "费力", "吃力", "困难", "简单");
	}

	private static bool ContainsUsabilityQuestionnaireContext(string text)
	{
		return ContainsAny(text, "usable", "usability", "easy to use", "good to use", "user friendly", "user-friendly", "try the system", "use the system", "people use it", "tell me if it is good", "system evaluation", "sus", "system usability scale", "好不好用", "好用不好用", "好用", "不好用", "易用", "易不易用", "可用性", "系统可用性", "让人用用", "让用户用", "用一用", "顺不顺手", "直观", "流畅", "系统评估");
	}

	private static bool ContainsQuestionnaireSystemContext(string text)
	{
		return ContainsAny(text, "questionnaire system", "survey system", "questionnaire platform", "survey platform", "form system", "response system", "answering system", "questionnaire app", "survey app", "detect answers", "detect responses", "response detection", "answer detection", "调查问卷系统", "问卷系统", "问卷平台", "调查系统", "表单系统", "作答系统", "答题系统", "回答系统", "作答检测", "回答检测", "响应检测");
	}

	private static bool ContainsResponseDetectionContext(string text)
	{
		return ContainsAny(text, "whether users answered", "whether user answered", "whether answered", "has answered", "answered or not", "did not answer", "not answered", "missing answer", "missing response", "response completion", "completion rate", "submit response", "submitted response", "detect if", "detect whether", "detect user", "detect users", "是否作答", "是否回答", "是否答题", "有没有作答", "有没有回答", "有没有答题", "未作答", "没作答", "漏答", "缺失回答", "作答完成", "回答完成", "提交答案", "提交问卷", "检测用户", "判断用户");
	}

	private static bool ContainsNewSystemEvaluationContext(string text)
	{
		return ContainsAny(text, "brand new system", "new system", "new prototype", "prototype", "first evaluation", "new application", "new app", "new xr system", "new vr system", "new immersive system", "newly built", "newly developed", "崭新的系统", "崭新系统", "全新系统", "新系统", "新的系统", "新原型", "原型", "刚做出来", "刚做的", "刚开发", "初次评估", "第一次评估");
	}

	private static bool ContainsTaskVariationContext(string text)
	{
		return ContainsAny(text, "different tasks", "multiple tasks", "several tasks", "various tasks", "task types", "tasks for users", "users do tasks", "people do tasks", "participants do tasks", "across tasks", "between tasks", "不同任务", "不同的任务", "多个任务", "多种任务", "各种任务", "任务类型", "让用户做任务", "让人做任务", "任务之间", "一系列任务");
	}

	private static bool ContainsUserExperienceQuestionnaireContext(string text)
	{
		return ContainsAny(text, "user experience", "ux", "overall experience", "vr experience", "experience quality", "vr evaluation", "evaluate vr", "evaluate virtual reality", "virtual reality evaluation", "enjoyment", "enjoyable", "satisfaction", "engagement", "engaging", "fun", "motivation", "vrsuq", "ux in ive", "ueq", "用户体验", "整体体验", "体验质量", "使用体验", "使用感受", "评估vr", "评估虚拟现实", "虚拟现实评估", "满意度", "喜欢", "有趣", "趣味", "参与感", "投入感", "吸引人");
	}

	private static bool ContainsPhysicalDemandQuestionnaireContext(string text)
	{
		return ContainsAny(text, "physical demand", "physical workload", "physical burden", "body burden", "perceived exertion", "exertion", "rpe", "borg", "fatigue", "tired", "muscle effort", "arm effort", "upper-limb", "upper limb", "arm movement", "rehabilitation training", "rehab training", "physical training", "motor training", "身体负担", "身体负荷", "体力负担", "体力负荷", "身体吃力", "用力", "费力", "体力消耗", "疲劳", "肌肉疲劳", "肌肉酸痛", "上肢运动", "上肢训练", "手臂运动", "手臂训练", "康复训练", "运动训练");
	}

	private static bool ContainsSafetyComfortQuestionnaireContext(string text)
	{
		return ContainsAny(text, "safety", "safe", "felt safe", "safety feeling", "comfort", "comfortable", "pain", "painful", "injury", "injury risk", "risk", "fall", "fall risk", "stable", "stability", "fear of movement", "fear of injury", "kinesiophobia", "rehabilitation safety", "clinical safety", "安全感", "安全", "使用安全", "感到安全", "舒适", "舒适度", "疼痛", "痛感", "痛", "受伤", "损伤", "风险", "跌倒", "摔倒", "稳定", "稳定感", "害怕运动", "害怕受伤");
	}

	private static bool ContainsContinuanceIntentionQuestionnaireContext(string text)
	{
		return ContainsAny(text, "willingness to continue", "continue using", "continued use", "future use", "intention to use", "usage intention", "adoption", "acceptance", "acceptability", "adherence", "compliance", "keep using", "use again", "tam", "utaut", "愿意继续使用", "想继续使用", "继续使用", "未来使用", "使用意愿", "继续使用意愿", "接受度", "接受意愿", "采纳", "采用", "依从性", "坚持使用", "再次使用");
	}

	private static bool ContainsMotionSicknessQuestionnaireContext(string text)
	{
		return ContainsAny(text, "motion sickness", "simulator sickness", "cybersickness", "vr sickness", "vrsq", "ssq", "nausea", "dizzy", "dizziness", "eye strain", "headache", "disorientation", "hmd discomfort", "vr discomfort", "tolerability", "symptoms", "晕动", "晕动症", "晕vr", "晕眩", "头晕", "恶心", "眼疲劳", "眼睛疲劳", "头痛", "定向障碍", "晕动不适", "虚拟现实不适", "耐受", "症状");
	}

	private static bool ContainsFullSsqRequest(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (!text2.Contains("ssq", StringComparison.OrdinalIgnoreCase) && !ContainsAny(text2, "simulator sickness questionnaire"))
		{
			return false;
		}
		return ContainsAny(text2, "full", "complete", "full version", "complete version", "full ssq", "16 item", "16-item", "16 symptoms", "完整版", "完整", "全版", "全量表", "全部题项");
	}

	private static bool ContainsSpatialPresenceQuestionnaireContext(string text)
	{
		return ContainsAny(text, "spatial presence", "place illusion", "being there", "sense of being there", "felt there", "inside the environment", "inside the virtual environment", "ipq", "igroup presence", "空间临场", "空间存在感", "地点错觉", "身临其境", "像在那里", "在虚拟环境里", "在里面");
	}

	private static bool ContainsSocialPresenceQuestionnaireContext(string text)
	{
		return ContainsAny(text, "social presence", "co-presence", "copresence", "other people", "another person", "virtual human", "virtual humans", "avatar interaction", "collaboration", "collaborative", "audience", "public speaking", "teammate", "partner", "multi-user", "multiplayer", "社交临场", "共在", "共同在场", "其他人", "虚拟人", "角色互动", "社交互动", "协作", "合作", "观众", "队友", "伙伴", "多人");
	}

	private static bool ContainsCollaborationQualityQuestionnaireContext(string text)
	{
		return ContainsAny(text, "communication", "communication quality", "smooth communication", "collaboration quality", "collaboration", "collaborative quality", "teamwork", "coordination", "coordination quality", "沟通", "沟通是否顺畅", "沟通顺畅", "协作质量", "协作是否顺畅", "协作顺畅", "合作质量", "配合");
	}

	private static bool ContainsSelfPresenceQuestionnaireContext(string text)
	{
		return ContainsAny(text, "self-presence", "self presence", "embodiment", "body ownership", "virtual body", "avatar body", "avatar", "agency", "self-location", "body agency", "virtual hands", "tracked hands", "hand tracking", "full body", "body tracking", "自我临场", "身体所有感", "身体拥有感", "化身", "虚拟身体", "虚拟角色", "虚拟手", "手部追踪", "身体追踪", "控制感", "代理感", "自我定位");
	}

	private static bool ContainsSimulatorRealismQuestionnaireContext(string text)
	{
		return ContainsAny(text, "simulator realism", "simulation realism", "training realism", "visual realism", "realistic simulator", "haptic realism", "realistic training", "fidelity", "surgical simulation", "medical simulator", "flight simulator", "driving simulator", "模拟器真实感", "模拟真实感", "训练真实感", "视觉真实感", "触觉真实感", "高保真", "保真度", "医疗模拟", "手术模拟", "驾驶模拟");
	}

	private static bool ContainsVirtualTherapistAllianceQuestionnaireContext(string text)
	{
		return ContainsAny(text, "virtual therapist", "therapist alliance", "therapeutic alliance", "virtual coach", "virtual counselor", "virtual counsellor", "therapy agent", "rehabilitation coach", "treatment goals", "therapy goals", "vtas", "虚拟治疗师", "治疗联盟", "虚拟教练", "虚拟咨询师", "康复教练", "治疗目标", "康复目标");
	}

	private static bool ContainsPresenceQuestionnaireContext(string text)
	{
		return ContainsAny(text, "presence", "immersion", "immersive", "spatial presence", "sense of presence", "virtual environment", "virtual world", "xr world", "vr world", "feeling there", "being there", "felt there", "inside the virtual", "沉浸", "沉浸感", "临场", "临场感", "身临其境", "虚拟环境", "虚拟世界", "xr世界", "vr世界", "在xr世界", "在里面", "真实感");
	}

	private static bool ContainsTrustQuestionnaireContext(string text)
	{
		return ContainsAny(text, "trust", "reliable", "reliability", "confidence in the system", "system confidence", "dependable", "predictable", "automation trust", "trust in automation", "信任", "可靠", "可信", "有信心", "可预期");
	}

	private static bool ContainsStressQuestionnaireContext(string text)
	{
		return ContainsAny(text, "stress", "pressure", "anxiety", "tension", "tense", "relaxation", "relaxed", "calm", "mental stress", "overwhelmed", "压力", "紧张", "焦虑", "放松", "放松感", "平静", "紧迫", "压迫", "崩溃");
	}

	private static bool ContainsPerformanceContext(string text)
	{
		return ContainsAny(text, "performance", "task performance", "accuracy", "completion", "completion time", "score", "task score", "result", "outcome", "success rate", "error rate", "breathing outcome", "breathing training outcome", "表现", "任务表现", "不同任务下的表现", "正确率", "完成度", "完成时间", "成绩", "得分", "任务结果", "错误率", "呼吸训练效果");
	}

	private static int CountDetectedQuestionnaireContexts(string text)
	{
		int num = 0;
		if (ContainsTaskDifficultyQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsMotionSicknessQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsUserExperienceQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsUsabilityQuestionnaireContext(text) || ContainsNewSystemEvaluationContext(text) || ContainsQuestionnaireSystemContext(text) || ContainsResponseDetectionContext(text))
		{
			num++;
		}
		if (ContainsPresenceQuestionnaireContext(text) || ContainsSpatialPresenceQuestionnaireContext(text) || ContainsSocialPresenceQuestionnaireContext(text) || ContainsSelfPresenceQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsTaskVariationContext(text))
		{
			num++;
		}
		if (ContainsPhysicalDemandQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsSafetyComfortQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsContinuanceIntentionQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsCollaborationQualityQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsSimulatorRealismQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsVirtualTherapistAllianceQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsTrustQuestionnaireContext(text))
		{
			num++;
		}
		if (ContainsStressQuestionnaireContext(text))
		{
			num++;
		}
		return num;
	}

	private static StudyConfig BuildTaskDifficultyRecommendation(string userRequest)
	{
		int num = ResolveRequestedScaleForConstruct(userRequest, "cognitive_load", 21);
		string text = ResolveRequestedResponseModeForConstruct(userRequest, "cognitive_load", num, "slider");
		StudyConfig obj = new StudyConfig
		{
			studyName = "PAXSM Cognitive Load Questionnaire",
			studyVersion = "1.0",
			design = "questionnaire-only",
			construct = "cognitive_load",
			participantId = "P001",
			participantNumber = 1,
			sessionNumber = 1,
			experimenterId = "",
			conditionLabel = "QuestionnaireOnly",
			conditions = new List<string>(),
			counterbalancingOrder = "None",
			randomizeQuestions = false,
			questionBankResourcesPath = "QuestionBanks/Scale",
			responseMode = text,
			scale = num,
			outputFolder = "C:/PAXSM_FullStudy_Data",
			outputSubfolder = "ExportsCSV",
			fileNamePrefix = "PAXSM",
			exportMergedCsv = true,
			exportRawStageEvents = true,
			exportOnQuit = true,
			preventOverwrite = true,
			naturalLanguageRequest = userRequest,
			generatedSummary = "我建议使用认知负荷问卷。不同任务或不同难度通常会带来不同的心理需求、努力程度和主观负担；因此使用 NASA-TLX 风格的 21 点 slider 可以帮助比较任务之间的感知负荷。"
		};
		ApplyInstrumentMetadata(obj);
		obj.generatedSummary = $"I recommend a cognitive-workload module for perceived task demand. Standardized options include NASA-TLX or SIM-TLX; the generated PAXSM bank is an adapted workload-ratings bank using {num}-point {text} responses.";
		NormalizeConfig(obj, userRequest);
		return obj;
	}

	private static StudyConfig BuildUsabilityRecommendation(string userRequest)
	{
		int num = ResolveRequestedScaleForConstruct(userRequest, "usability", 5);
		string text = ResolveRequestedResponseModeForConstruct(userRequest, "usability", num, "card");
		StudyConfig obj = new StudyConfig
		{
			studyName = "PAXSM Usability Questionnaire",
			studyVersion = "1.0",
			design = "questionnaire-only",
			construct = "usability",
			participantId = "P001",
			participantNumber = 1,
			sessionNumber = 1,
			experimenterId = "",
			conditionLabel = "QuestionnaireOnly",
			conditions = new List<string>(),
			counterbalancingOrder = "None",
			randomizeQuestions = false,
			questionBankResourcesPath = "QuestionBanks/Scale",
			responseMode = text,
			scale = num,
			outputFolder = "C:/PAXSM_FullStudy_Data",
			outputSubfolder = "ExportsCSV",
			fileNamePrefix = "PAXSM",
			exportMergedCsv = true,
			exportRawStageEvents = true,
			exportOnQuit = true,
			preventOverwrite = true,
			naturalLanguageRequest = userRequest,
			generatedSummary = "I recommend a system-usability module for this evaluation."
		};
		ApplyInstrumentMetadata(obj);
		obj.generatedSummary = $"I recommend a system-usability module for ease of use and confidence. Standardized options include SUS or UMUX-Lite; the generated PAXSM bank is adapted unless exact standard wording and scoring are preserved. It will use {num}-point {text} responses.";
		NormalizeConfig(obj, userRequest);
		return obj;
	}

	private static StudyConfig BuildQuestionnaireSystemUsabilityRecommendation(string userRequest)
	{
		StudyConfig studyConfig = BuildUsabilityRecommendation(userRequest);
		studyConfig.studyName = "PAXSM Questionnaire System Usability";
		ApplyInstrumentMetadata(studyConfig);
		studyConfig.generatedSummary = $"I recommend a system-usability module for the questionnaire or response-detection system, using {studyConfig.scale}-point {studyConfig.responseMode} responses. Treat answer detection itself as objective validation, not as a psychological questionnaire construct.";
		NormalizeConfig(studyConfig, userRequest);
		return studyConfig;
	}

	private static AgentResult? TryBuildContextRecommendation(string userRequest, string source)
	{
		AgentResult agentResult = TryBuildObjectiveMetricsOnlyResult(userRequest, source);
		if (agentResult != null)
		{
			return agentResult;
		}
		AgentResult agentResult2 = TryBuildResearcherSuppliedQuestionnaireResult(userRequest, source);
		if (agentResult2 != null)
		{
			return agentResult2;
		}
		AgentResult agentResult3 = TryBuildBroadSystemEvaluationRecommendation(userRequest, source);
		if (agentResult3 != null)
		{
			return agentResult3;
		}
		StudyConfig studyConfig = null;
		string text = DetectUiFilterConstruct(userRequest);
		if (!string.IsNullOrWhiteSpace(text))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, text);
		}
		else if (ContainsFullSsqRequest(userRequest) || ContainsExactSsqRequest(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "motion_sickness");
		}
		else if (ContainsMotionSicknessQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "motion_sickness");
		}
		else if (ContainsSpatialPresenceQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "presence_spatial");
		}
		else if (ContainsSocialPresenceQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "presence_social");
		}
		else if (ContainsSelfPresenceQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "presence_self");
		}
		else if (ContainsPresenceQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "presence_combined");
		}
		else if (ContainsUserExperienceQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "user_experience");
		}
		else if (ContainsPhysicalDemandQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "physical_demand");
		}
		else if (ContainsSafetyComfortQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "safety_comfort");
		}
		else if (ContainsContinuanceIntentionQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "continuance_intention");
		}
		else if (ContainsSimulatorRealismQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "simulator_realism");
		}
		else if (ContainsVirtualTherapistAllianceQuestionnaireContext(userRequest))
		{
			studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "virtual_therapist_alliance");
		}
		else if (ContainsQuestionnaireSystemContext(userRequest) || ContainsResponseDetectionContext(userRequest))
		{
			studyConfig = BuildQuestionnaireSystemUsabilityRecommendation(userRequest);
		}
		else if (ContainsUsabilityQuestionnaireContext(userRequest) || ContainsNewSystemEvaluationContext(userRequest))
		{
			studyConfig = BuildUsabilityRecommendation(userRequest);
		}
		else if (ContainsTaskDifficultyQuestionnaireContext(userRequest) || ContainsTaskVariationContext(userRequest))
		{
			studyConfig = BuildTaskDifficultyRecommendation(userRequest);
		}
		if (studyConfig == null)
		{
			return null;
		}
		List<StudyConfig> list = BuildQuestionnaireTaskConfigs(userRequest, studyConfig);
		List<string> warnings = MergeWarnings(BuildWarningsForTaskConfigs(list), BuildContextWarnings(userRequest));
		return new AgentResult
		{
			Config = ((list.Count > 0) ? list[0] : studyConfig),
			Source = source,
			Summary = BuildTaskSetSummary(userRequest, studyConfig.generatedSummary, list),
			Warnings = warnings,
			TaskConfigs = list
		};
	}

	private static AgentResult? TryBuildObjectiveMetricsOnlyResult(string userRequest, string source)
	{
		string text = (userRequest ?? "").ToLowerInvariant();
		if ((!ContainsAny(text, "no subjective", "not subjective", "do not ask subjective", "without subjective", "不需要问主观", "不问主观", "不要主观感受", "不需要主观感受", "不需要问卷", "不用问卷") && !Regex.IsMatch(text, "\\u4e0d\\s*\\u9700\\u8981\\s*\\u95ee\\s*\\u4e3b\\u89c2", RegexOptions.IgnoreCase) && !Regex.IsMatch(text, "\\u4e0d\\s*\\u95ee\\s*\\u4e3b\\u89c2", RegexOptions.IgnoreCase)) || !ContainsPerformanceContext(userRequest ?? ""))
		{
			return null;
		}
		return new AgentResult
		{
			Source = source,
			NeedsClarification = true,
			Summary = "This request is about objective Unity-side task logging, not a psychological questionnaire. Configure task-metric export for accuracy, reaction time, timeout, completion status, and any task score/outcome; PAXSM questionnaire tasks should remain empty unless you also want a subjective measure.",
			Questions = new List<string> { "Do you also want a subjective questionnaire such as workload, usability, or user experience, or should PAXSM only keep objective logs?" },
			Warnings = new List<string> { "Accuracy, reaction time, timeout, and completion are objective task metrics rather than questionnaire constructs.", "PAXSM can keep reaction-time and raw stage-event logs, but task-specific accuracy/timeout/completion scoring must be implemented or verified on the Unity task side." },
			TaskConfigs = new List<StudyConfig>()
		};
	}

	private static AgentResult? TryBuildResearcherSuppliedQuestionnaireResult(string userRequest, string source)
	{
		if (ContainsResearcherImportedStandardQuestionnaire(userRequest))
		{
			StudyConfig studyConfig = BuildFilteredQuestionnaireRecommendation(userRequest, "usability");
			studyConfig.instrumentName = "Researcher-supplied System Usability Scale (SUS)";
			studyConfig.instrumentStatus = "researcher_supplied_standardized_candidate";
			studyConfig.recommendedStandardInstrument = "System Usability Scale (SUS)";
			studyConfig.recommendationRationale = "The researcher says the complete SUS wording and 5-point response scale have already been imported; PAXSM should deploy it and record paradata rather than regenerating an adapted usability bank.";
			studyConfig.generatedSummary = "Configured the researcher-supplied SUS candidate for deployment. PAXSM should record item response time, answer changes, completion status, and questionnaire duration. Verify original wording, response scale, reverse scoring, and 0-100 scoring before reporting standardized SUS results.";
			NormalizeConfig(studyConfig, userRequest);
			return new AgentResult
			{
				Config = studyConfig,
				Source = source,
				Summary = studyConfig.generatedSummary,
				Warnings = MergeWarnings(BuildWarnings(studyConfig), new List<string> { "Researcher must verify original SUS wording, 5-point response scale, reverse scoring, and 0-100 scoring before reporting standardized SUS results.", "Paradata to record: item response time, answer changes, completion status, and questionnaire duration." }),
				TaskConfigs = new List<StudyConfig> { studyConfig }
			};
		}
		if (!ContainsResearcherSuppliedCustomItems(userRequest))
		{
			return null;
		}
		StudyConfig studyConfig2 = BuildFilteredQuestionnaireRecommendation(userRequest, "user_experience");
		studyConfig2.instrumentName = "Researcher-supplied custom XR experience feedback";
		studyConfig2.instrumentStatus = "researcher_supplied_custom";
		studyConfig2.recommendedStandardInstrument = "Supplemental custom items; use UEQ-S/UEQ, SUS, IPQ, or VRNQ separately if standardized comparability is required.";
		studyConfig2.recommendationRationale = "The researcher supplied the item content, so PAXSM should deploy those items without claiming they are a validated standardized questionnaire.";
		studyConfig2.generatedSummary = "Configured the researcher-supplied 8-item custom XR experience feedback task. This is not a validated standardized questionnaire and should not be reported as SUS, UEQ, IPQ, VRNQ, or similar. It can be used as supplemental system-evaluation feedback.";
		NormalizeConfig(studyConfig2, userRequest);
		return new AgentResult
		{
			Config = studyConfig2,
			Source = source,
			Summary = studyConfig2.generatedSummary,
			Warnings = MergeWarnings(BuildWarnings(studyConfig2), new List<string> { "This is not a validated standardized questionnaire.", "Do not report the supplied items as SUS, UEQ, IPQ, VRNQ, or another validated scale unless the original instrument has been imported and verified." }),
			TaskConfigs = new List<StudyConfig> { studyConfig2 }
		};
	}

	private static AgentResult? TryBuildBroadSystemEvaluationRecommendation(string userRequest, string source)
	{
		if (!ShouldUseBroadSystemEvaluationDefaults(userRequest))
		{
			return null;
		}
		List<StudyConfig> list = new List<StudyConfig>();
		AddUniqueTaskConfig(list, BuildUsabilityRecommendation(userRequest));
		AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "user_experience"));
		if (ContainsXrOrImmersiveSystemContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "presence_combined"));
		}
		List<string> list2 = BuildWarningsForTaskConfigs(list);
		if (!ContainsXrOrImmersiveSystemContext(userRequest))
		{
			list2.Add("If this system is XR/VR or intentionally immersive, also add a presence / immersion questionnaire. If task difficulty is central, add cognitive load separately.");
		}
		return new AgentResult
		{
			Config = list[0],
			Source = source,
			Summary = BuildBroadSystemEvaluationSummary(userRequest, list),
			Warnings = list2,
			TaskConfigs = list
		};
	}

	private static string BuildBroadSystemEvaluationSummary(string userRequest, List<StudyConfig> taskConfigs)
	{
		List<string> list = new List<string>();
		foreach (StudyConfig taskConfig in taskConfigs)
		{
			list.Add(QuestionnaireTaskLabel(taskConfig));
		}
		string text = "For a broad system evaluation, I recommend a core battery first: " + JoinHumanReadable(list) + ".";
		text += " Usability covers whether the system is easy, learnable, and confidence-building. General XR/user experience captures satisfaction, enjoyment, engagement, feedback clarity, comfort, and willingness to continue.";
		text = ((!ContainsXrOrImmersiveSystemContext(userRequest)) ? (text + " If the system is specifically XR/VR or immersive, add a validated presence option such as IPQ, Presence Questionnaire, ITC-SOPI, or SUS-PQ, or keep the PAXSM bank clearly labeled as custom/adapted.") : (text + " Because the request mentions an XR/VR or immersive context, presence is included as a conditional module to capture the sense of being in the virtual environment."));
		return text + " Add workload only when task demand is central, cybersickness only when HMD/motion discomfort is plausible, and embodiment/agency only when avatars, tracked hands, or body interaction matter.";
	}

	private static string DetectUiFilterConstruct(string userRequest)
	{
		Match match = Regex.Match(userRequest, "Required construct:\\s*([a-zA-Z_]+)", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return "";
		}
		return match.Groups[1].Value.Trim().ToLowerInvariant();
	}

	private unsafe static StudyConfig BuildFilteredQuestionnaireRecommendation(string userRequest, string construct)
	{
		object obj = construct switch
		{
			"cognitive_load" => (21, "slider", "Cognitive workload module"), 
			"usability" => (5, "card", "System usability module"), 
			"user_experience" => (7, "card", "General XR experience feedback"), 
			"physical_demand" => (7, "card", "Physical demand / perceived exertion module"), 
			"safety_comfort" => (7, "card", "Safety and comfort module"), 
			"continuance_intention" => (7, "card", "Continuance intention module"), 
			"presence" => (7, "card", "Presence / immersion module"), 
			"presence_spatial" => (7, "card", "Spatial presence module"), 
			"presence_social" => (7, "card", "Social presence module"), 
			"collaboration_quality" => (7, "card", "Collaboration / communication quality module"), 
			"presence_self" => (7, "card", "Self-presence / embodiment module"), 
			"presence_combined" => (7, "card", "Combined presence module"), 
			"motion_sickness" => (4, "card", "Cybersickness / discomfort screen"), 
			"simulator_realism" => (7, "card", "Simulator realism module"), 
			"immersion" => (7, "card", "Immersion / involvement module"), 
			"embodiment" => (7, "card", "Embodiment / agency module"), 
			"virtual_therapist_alliance" => (7, "card", "Virtual therapist alliance module"), 
			"trust" => (7, "card", "Trust module"), 
			"stress" => (7, "card", "Stress / pressure module"), 
			_ => (7, "card", "Custom questionnaire module"), 
		};
		int item = ((ValueTuple<int, string, string>*)(&obj))->Item1;
		string item2 = ((ValueTuple<int, string, string>*)(&obj))->Item2;
		string item3 = ((ValueTuple<int, string, string>*)(&obj))->Item3;
		int num = ResolveRequestedScaleForConstruct(userRequest, construct, item);
		string text = ResolveRequestedResponseModeForConstruct(userRequest, construct, num, item2);
		StudyConfig obj2 = new StudyConfig
		{
			studyName = "PAXSM Questionnaire",
			studyVersion = "1.0",
			design = "questionnaire-only",
			construct = construct,
			participantId = "P001",
			participantNumber = 1,
			sessionNumber = 1,
			experimenterId = "",
			conditionLabel = "QuestionnaireOnly",
			conditions = new List<string>(),
			counterbalancingOrder = "None",
			randomizeQuestions = false,
			questionBankResourcesPath = "QuestionBanks/Scale",
			responseMode = text,
			scale = num,
			outputFolder = "C:/PAXSM_FullStudy_Data",
			outputSubfolder = "ExportsCSV",
			fileNamePrefix = "PAXSM",
			exportMergedCsv = true,
			exportRawStageEvents = true,
			exportOnQuit = true,
			preventOverwrite = true,
			naturalLanguageRequest = userRequest,
			generatedSummary = $"Using the selected questionnaire task: {item3}, with {num}-point {text} responses."
		};
		ApplyInstrumentMetadata(obj2);
		NormalizeConfig(obj2, userRequest);
		return obj2;
	}

	private static List<StudyConfig> BuildQuestionnaireTaskConfigs(string userRequest, StudyConfig primaryConfig)
	{
		List<StudyConfig> list = new List<StudyConfig>();
		List<string> list2 = SplitCompoundConstructs(primaryConfig.construct);
		bool flag = LooksLikeCompoundConstruct(primaryConfig.construct);
		if (list2.Count > 1)
		{
			foreach (string item in list2)
			{
				AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, item));
			}
		}
		if (ResearcherRequestedCustomInstrument(userRequest) && ContainsUserExperienceQuestionnaireContext(userRequest) && !ContainsResearcherSuppliedCustomItems(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "user_experience"));
			return list;
		}
		if (ContainsMotionSicknessQuestionnaireContext(userRequest) || ContainsFullSsqRequest(userRequest) || ContainsExactSsqRequest(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "motion_sickness"));
		}
		if (ContainsSpatialPresenceQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "presence_spatial"));
		}
		if (ContainsSocialPresenceQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "presence_social"));
		}
		if (ContainsSelfPresenceQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "presence_self"));
		}
		if (ContainsPresenceQuestionnaireContext(userRequest) && !ContainsSpatialPresenceQuestionnaireContext(userRequest) && !ContainsSelfPresenceQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "presence_combined"));
		}
		if (ContainsCollaborationQualityQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "collaboration_quality"));
		}
		if (ContainsUserExperienceQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "user_experience"));
		}
		if (ContainsPhysicalDemandQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "physical_demand"));
		}
		if (ContainsSafetyComfortQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "safety_comfort"));
		}
		if (ContainsContinuanceIntentionQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "continuance_intention"));
		}
		if (ContainsQuestionnaireSystemContext(userRequest) || ContainsResponseDetectionContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildQuestionnaireSystemUsabilityRecommendation(userRequest));
		}
		else if (ContainsUsabilityQuestionnaireContext(userRequest) || ContainsNewSystemEvaluationContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildUsabilityRecommendation(userRequest));
		}
		if (ContainsTaskDifficultyQuestionnaireContext(userRequest) || ContainsTaskVariationContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildTaskDifficultyRecommendation(userRequest));
		}
		if (ContainsSimulatorRealismQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "simulator_realism"));
		}
		if (ContainsVirtualTherapistAllianceQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "virtual_therapist_alliance"));
		}
		if (ContainsTrustQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "trust"));
		}
		if (ContainsStressQuestionnaireContext(userRequest))
		{
			AddUniqueTaskConfig(list, BuildFilteredQuestionnaireRecommendation(userRequest, "stress"));
		}
		bool flag2 = list.Count > 0;
		if (!flag && (!flag2 || ShouldKeepPrimaryConfig(userRequest, primaryConfig)))
		{
			AddUniqueTaskConfig(list, primaryConfig);
		}
		if (list.Count == 0)
		{
			AddUniqueTaskConfig(list, primaryConfig);
		}
		return list;
	}

	private static bool ShouldKeepPrimaryConfig(string userRequest, StudyConfig primaryConfig)
	{
		string text = QuestionnaireTaskKey(primaryConfig.construct);
		if (string.IsNullOrWhiteSpace(text) || text.Equals("custom", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return text switch
		{
			"cognitive_load" => ContainsTaskDifficultyQuestionnaireContext(userRequest) || ContainsTaskVariationContext(userRequest), 
			"usability" => ContainsUsabilityQuestionnaireContext(userRequest) || ContainsNewSystemEvaluationContext(userRequest) || ContainsQuestionnaireSystemContext(userRequest) || ContainsResponseDetectionContext(userRequest), 
			"user_experience" => ContainsUserExperienceQuestionnaireContext(userRequest), 
			"physical_demand" => ContainsPhysicalDemandQuestionnaireContext(userRequest), 
			"safety_comfort" => ContainsSafetyComfortQuestionnaireContext(userRequest), 
			"continuance_intention" => ContainsContinuanceIntentionQuestionnaireContext(userRequest), 
			"motion_sickness" => ContainsMotionSicknessQuestionnaireContext(userRequest), 
			"presence_spatial" => ContainsSpatialPresenceQuestionnaireContext(userRequest), 
			"presence_social" => ContainsSocialPresenceQuestionnaireContext(userRequest), 
			"presence_self" => ContainsSelfPresenceQuestionnaireContext(userRequest), 
			"presence_combined" => ContainsPresenceQuestionnaireContext(userRequest), 
			"simulator_realism" => ContainsSimulatorRealismQuestionnaireContext(userRequest), 
			"virtual_therapist_alliance" => ContainsVirtualTherapistAllianceQuestionnaireContext(userRequest), 
			"trust" => ContainsTrustQuestionnaireContext(userRequest), 
			"stress" => ContainsStressQuestionnaireContext(userRequest), 
			_ => false, 
		};
	}

	private static void AddUniqueTaskConfig(List<StudyConfig> taskConfigs, StudyConfig config)
	{
		NormalizeConfig(config, config.naturalLanguageRequest);
		ApplyInstrumentMetadata(config);
		string value = QuestionnaireTaskKey(config.construct);
		foreach (StudyConfig taskConfig in taskConfigs)
		{
			if (QuestionnaireTaskKey(taskConfig.construct).Equals(value, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		taskConfigs.Add(config);
	}

	private static void ApplyInstrumentMetadata(StudyConfig config, bool force = false)
	{
		string text = (config.construct ?? "").Trim().ToLowerInvariant();
		bool flag = ResearcherRejectedCustomInstrument(config.naturalLanguageRequest);
		bool flag2 = ResearcherRequestedCustomInstrument(config.naturalLanguageRequest);
		(string, string, string, string, string) tuple;
		switch (text)
		{
		case "cognitive_load":
			tuple = ("Core", "NASA-TLX or SIM-TLX", "adapted_short_form", "NASA-TLX or SIM-TLX", "Use an existing workload questionnaire family when the study includes task difficulty, effort, time pressure, or performance demand. PAXSM records an adapted implementation unless the original wording, response format, and scoring are preserved.");
			break;
		case "usability":
			tuple = ("Core", "SUS or UMUX-Lite", (config.scale == 5 && string.Equals(config.responseMode, "card", StringComparison.OrdinalIgnoreCase)) ? "standardized_available" : "adapted_short_form", "SUS, UMUX-Lite, or UEQ pragmatic quality", "Use an existing usability questionnaire as a baseline for whether the system is usable, learnable, and confidence-building.");
			break;
		case "user_experience":
			tuple = ("Core", "UEQ-S/UEQ or AttrakDiff", "standardized_recommended_not_deployed", "UEQ-S/UEQ, AttrakDiff, or a validated XR UX scale if one fits the study", "Use an existing UX questionnaire family for satisfaction, enjoyment, engagement, feedback clarity, and hedonic/pragmatic quality.");
			break;
		case "physical_demand":
			tuple = ("Core for rehabilitation", "Borg RPE or NASA-TLX Physical Demand", "standardized_recommended_not_deployed", "Borg RPE, NASA-TLX Physical Demand, fatigue, exertion, or pain scales", "Use an existing exertion or physical-demand measure when the study involves rehabilitation movement, upper-limb exercise, physical burden, fatigue, or perceived exertion.");
			break;
		case "safety_comfort":
			tuple = ("Core for rehabilitation safety", "Rehabilitation safety, pain, fatigue, comfort, or fear-of-injury measures", "standardized_recommended_not_deployed", "Validated rehabilitation safety, pain, fatigue, comfort, or fear-of-injury measures if available", "Use target-population-appropriate existing safety, pain, fatigue, comfort, or fear-of-injury measures when participants need to judge whether movement felt safe, stable, comfortable, and low risk.");
			break;
		case "continuance_intention":
			tuple = ("Core / adoption", "TAM/UTAUT-style continuance intention", "standardized_recommended_not_deployed", "TAM/UTAUT-style intention-to-use items, adherence, acceptance, or rehabilitation adoption measures", "Use an existing acceptance/adoption questionnaire family when the study asks whether participants would keep using the system after the session.");
			break;
		case "presence_combined":
		case "presence":
			tuple = ("Conditional module: immersive XR", "IPQ, Presence Questionnaire, ITC-SOPI, or SUS-PQ", "standardized_recommended_not_deployed", "IPQ, Presence Questionnaire, ITC-SOPI, or SUS-PQ", "Use an existing presence questionnaire when immersion, virtual environment presence, or sense of being there is central.");
			break;
		case "presence_spatial":
			tuple = ("Conditional module: spatial presence", "IPQ or Presence Questionnaire spatial presence items", "standardized_recommended_not_deployed", "IPQ or Presence Questionnaire spatial presence items", "Use an existing spatial-presence measure when the study focuses on feeling located inside a virtual place.");
			break;
		case "presence_social":
			tuple = ("Core", "Networked Minds or social/co-presence scales", "standardized_recommended_not_deployed", "Networked Minds, co-presence, or social presence scales", "Use an existing social-presence measure when users interact with virtual humans, collaborators, audiences, or agents.");
			break;
		case "collaboration_quality":
			tuple = ("Core", "Communication and collaboration quality module", "custom_generated", "Use validated team communication, collaboration quality, or social interaction scales if those are central paper claims.", "Use a dedicated communication/collaboration module when the researcher explicitly asks whether communication was smooth or collaboration worked well.");
			break;
		case "presence_self":
		case "embodiment":
			tuple = ("Conditional module: body / agency", "VEQ, body ownership, embodiment, or agency scales", "standardized_recommended_not_deployed", "VEQ, body ownership, embodiment, or agency scales", "Use an existing embodiment or agency measure when avatars, virtual hands, body tracking, or agency are part of the system.");
			break;
		case "motion_sickness":
			tuple = ("Conditional module: HMD comfort", "FMS, SSQ, or VRSQ", "adapted_short_form", "FMS for quick checks, SSQ or VRSQ for fuller symptom measurement", "Use an existing cybersickness/discomfort measure when participants wear an HMD, experience motion, or may experience discomfort.");
			break;
		case "simulator_realism":
			tuple = ("Conditional module: simulation realism", "Domain-specific simulation realism or fidelity scale", "standardized_recommended_not_deployed", "Domain-specific simulation realism or fidelity scales", "Use a domain-specific simulation realism or fidelity measure for training, medical, driving, surgical, or high-fidelity simulation systems.");
			break;
		case "immersion":
			tuple = ("Conditional module: involvement", "IEQ or validated immersion/involvement measure", "standardized_recommended_not_deployed", "IEQ or other validated immersion/involvement measures", "Use an existing immersion/involvement measure when absorption, attention, or loss of awareness of the real world is the target.");
			break;
		case "virtual_therapist_alliance":
			tuple = ("Conditional module: therapeutic alliance", "VTAS or therapeutic alliance measure adapted for virtual agents", "standardized_recommended_not_deployed", "VTAS or therapeutic alliance measures adapted for virtual agents", "Use an existing therapeutic-alliance measure when a virtual therapist, coach, or rehabilitation guide is part of the system.");
			break;
		case "trust":
			tuple = ("Core", "Validated trust in automation/system trust scale", "standardized_recommended_not_deployed", "Validated trust in automation/system trust scales", "Use an existing trust questionnaire when reliability, predictability, automation, or confidence in system behavior matters.");
			break;
		case "stress":
			tuple = ("Conditional module: stress", "STAI, PSS, SAM arousal, or domain-specific stress measure", "standardized_recommended_not_deployed", "STAI, PSS, SAM arousal, or domain-specific stress measures", "Use an existing stress/anxiety/arousal measure when pressure, anxiety, tension, or relaxation is an outcome.");
			break;
		default:
			tuple = ("Custom", "Custom PAXSM questionnaire bank", "custom_generated", "Choose a validated scale if the construct is central to the paper claim.", "Use only when no established questionnaire fits the research question.");
			break;
		}
		var (text2, instrumentName, instrumentStatus, recommendedStandardInstrument, recommendationRationale) = tuple;
		if (flag2 && !text.Equals("custom", StringComparison.OrdinalIgnoreCase))
		{
			instrumentName = "Custom PAXSM questionnaire bank";
			instrumentStatus = "custom_generated";
			recommendationRationale = "The researcher explicitly requested a custom/self-made questionnaire; otherwise PAXSM prefers existing validated or widely used questionnaire families.";
		}
		if (IsExplicitConstructTarget(config.naturalLanguageRequest, text) && text2.StartsWith("Conditional", StringComparison.OrdinalIgnoreCase))
		{
			text2 = "Core";
		}
		if (flag)
		{
			switch (text)
			{
			case "usability":
				text2 = "Core";
				instrumentName = "System Usability Scale (SUS)";
				instrumentStatus = ((config.scale == 5 && string.Equals(config.responseMode, "card", StringComparison.OrdinalIgnoreCase)) ? "standardized_available" : "standardized_recommended_not_deployed");
				recommendedStandardInstrument = "System Usability Scale (SUS)";
				recommendationRationale = "The researcher rejected custom questionnaires, so the usability module is mapped to SUS rather than a self-made usability bank.";
				break;
			case "presence":
			case "presence_combined":
				text2 = (IsExplicitConstructTarget(config.naturalLanguageRequest, text) ? "Core" : "Conditional module: immersive XR");
				instrumentName = "Igroup Presence Questionnaire (IPQ)";
				instrumentStatus = "standardized_recommended_not_deployed";
				recommendedStandardInstrument = "Igroup Presence Questionnaire (IPQ), Presence Questionnaire, ITC-SOPI, or SUS-PQ";
				recommendationRationale = "The researcher rejected custom questionnaires, so the immersion/presence target is mapped to an existing presence questionnaire family. Use exact IPQ items and scoring if a fully standardized implementation is required.";
				break;
			case "motion_sickness":
				text2 = (IsExplicitConstructTarget(config.naturalLanguageRequest, text) ? "Core" : "Conditional module: simulator sickness");
				instrumentName = "Simulator Sickness Questionnaire (SSQ), full 16-item checklist";
				instrumentStatus = ((config.scale == 4 && string.Equals(config.responseMode, "card", StringComparison.OrdinalIgnoreCase)) ? "standardized_available" : "standardized_recommended_not_deployed");
				recommendedStandardInstrument = "Simulator Sickness Questionnaire (SSQ), full version";
				recommendationRationale = "The researcher rejected custom questionnaires, so the cybersickness target is mapped to the full SSQ rather than a custom discomfort screen.";
				break;
			}
		}
		if (text.Equals("usability", StringComparison.OrdinalIgnoreCase) && ContainsExactSusRequest(config.naturalLanguageRequest))
		{
			text2 = "Core";
			instrumentName = "System Usability Scale (SUS)";
			instrumentStatus = ((config.scale == 5 && string.Equals(config.responseMode, "card", StringComparison.OrdinalIgnoreCase)) ? "standardized_available" : "standardized_recommended_not_deployed");
			recommendedStandardInstrument = "System Usability Scale (SUS)";
			recommendationRationale = "The researcher explicitly requested SUS, so PAXSM should configure the 10-item, 5-point System Usability Scale and preserve reverse scoring plus 0-100 scoring rather than recommending alternatives.";
		}
		if (text.Equals("cognitive_load", StringComparison.OrdinalIgnoreCase) && ContainsExactNasaTlxRequest(config.naturalLanguageRequest))
		{
			text2 = "Core";
			instrumentName = "NASA Task Load Index (NASA-TLX)";
			instrumentStatus = ((config.scale == 21 && string.Equals(config.responseMode, "slider", StringComparison.OrdinalIgnoreCase)) ? "adapted_short_form" : "standardized_recommended_not_deployed");
			recommendedStandardInstrument = "NASA Task Load Index (NASA-TLX)";
			recommendationRationale = "The researcher explicitly requested NASA-TLX, so PAXSM should configure the six NASA-TLX workload dimensions. If pairwise weighting is omitted, report this as Raw NASA-TLX / unweighted NASA-TLX.";
		}
		if (text.Equals("motion_sickness", StringComparison.OrdinalIgnoreCase) && (ContainsFullSsqRequest(config.naturalLanguageRequest) || ContainsExactSsqRequest(config.naturalLanguageRequest)))
		{
			text2 = "Conditional module: simulator sickness";
			instrumentName = "Simulator Sickness Questionnaire (SSQ), full 16-item checklist";
			instrumentStatus = ((config.scale == 4 && string.Equals(config.responseMode, "card", StringComparison.OrdinalIgnoreCase)) ? "standardized_available" : "standardized_recommended_not_deployed");
			recommendedStandardInstrument = "Simulator Sickness Questionnaire (SSQ), full version";
			recommendationRationale = "The researcher explicitly requested SSQ, so PAXSM should configure the simulator sickness questionnaire rather than recommending alternative cybersickness screens.";
		}
		if (text.Equals("user_experience", StringComparison.OrdinalIgnoreCase) && ContainsExactUeqSRequest(config.naturalLanguageRequest))
		{
			text2 = "Core";
			instrumentName = "UEQ-S";
			instrumentStatus = "standardized_recommended_not_deployed";
			recommendedStandardInstrument = "User Experience Questionnaire - Short Form (UEQ-S)";
			recommendationRationale = "The researcher explicitly requested UEQ-S. Deploy full UEQ-S only if the original wording, bipolar response scale, and scoring are imported and verified; otherwise treat the current PAXSM task as standard import/verification required.";
		}
		if (text.Equals("cognitive_load", StringComparison.OrdinalIgnoreCase) && ContainsSingleItemWorkloadRequest(config.naturalLanguageRequest))
		{
			text2 = "Core";
			instrumentName = "Single-item cognitive workload / NASA-TLX Mental Demand rating";
			instrumentStatus = "adapted_short_form";
			recommendedStandardInstrument = "NASA-TLX Mental Demand, Raw NASA-TLX, or SIM-TLX";
			recommendationRationale = "The researcher explicitly requested one mental-demand item. This is not full NASA-TLX; it measures mental demand as a single-item workload rating.";
		}
		bool flag3 = ContainsVagueInstrumentLabel(config.recommendationRole, config.instrumentName, config.instrumentStatus, config.recommendedStandardInstrument);
		bool flag4 = ContainsAny((config.instrumentStatus ?? "").ToLowerInvariant(), "researcher_supplied_custom", "researcher_supplied_standardized_candidate");
		bool flag5 = IsLegacyInstrumentStatus(config.instrumentStatus) && !flag4;
		bool flag6 = !flag2 && !flag4 && IsExistingQuestionnaireBackedConstruct(text) && (ContainsAny((config.instrumentStatus ?? "").ToLowerInvariant(), "custom") || ContainsAny((config.instrumentName ?? "").ToLowerInvariant(), "custom", "paxsm generated", "generated bank"));
		if (force || flag3 || flag6 || string.IsNullOrWhiteSpace(config.recommendationRole))
		{
			config.recommendationRole = text2;
		}
		if (force || flag3 || flag6 || string.IsNullOrWhiteSpace(config.instrumentName))
		{
			config.instrumentName = instrumentName;
		}
		if (force || flag3 || flag6 || flag5 || string.IsNullOrWhiteSpace(config.instrumentStatus))
		{
			config.instrumentStatus = instrumentStatus;
		}
		if (force || flag3 || flag6 || string.IsNullOrWhiteSpace(config.recommendedStandardInstrument))
		{
			config.recommendedStandardInstrument = recommendedStandardInstrument;
		}
		if (force || flag3 || flag6 || string.IsNullOrWhiteSpace(config.recommendationRationale))
		{
			config.recommendationRationale = recommendationRationale;
		}
	}

	private static bool IsExplicitConstructTarget(string? userRequest, string construct)
	{
		string text = userRequest ?? "";
		switch ((construct ?? "").Trim().ToLowerInvariant())
		{
		case "cognitive_load":
			return ContainsTaskDifficultyQuestionnaireContext(text) || ContainsExactNasaTlxRequest(text);
		case "usability":
			return ContainsUsabilityQuestionnaireContext(text) || ContainsExactSusRequest(text);
		case "user_experience":
			return ContainsUserExperienceQuestionnaireContext(text) || ContainsExactUeqSRequest(text);
		case "physical_demand":
			return ContainsPhysicalDemandQuestionnaireContext(text);
		case "safety_comfort":
			return ContainsSafetyComfortQuestionnaireContext(text);
		case "continuance_intention":
			return ContainsContinuanceIntentionQuestionnaireContext(text);
		case "presence_combined":
		case "presence":
			return ContainsPresenceQuestionnaireContext(text);
		case "presence_spatial":
			return ContainsSpatialPresenceQuestionnaireContext(text);
		case "presence_social":
			return ContainsSocialPresenceQuestionnaireContext(text);
		case "presence_self":
		case "embodiment":
			return ContainsSelfPresenceQuestionnaireContext(text);
		case "motion_sickness":
			return ContainsMotionSicknessQuestionnaireContext(text) || ContainsExactSsqRequest(text);
		case "simulator_realism":
			return ContainsSimulatorRealismQuestionnaireContext(text);
		case "immersion":
			return ContainsPresenceQuestionnaireContext(text) || ContainsAny(text.ToLowerInvariant(), "immersion", "immersive", "沉浸");
		case "virtual_therapist_alliance":
			return ContainsVirtualTherapistAllianceQuestionnaireContext(text);
		case "trust":
			return ContainsTrustQuestionnaireContext(text);
		case "stress":
			return ContainsStressQuestionnaireContext(text);
		case "collaboration_quality":
			return ContainsCollaborationQualityQuestionnaireContext(text);
		default:
			return false;
		}
	}

	private static bool IsLegacyInstrumentStatus(string? status)
	{
		switch ((status ?? "").Trim().ToLowerInvariant())
		{
		case "standardized":
		case "adapted":
		case "custom":
			return true;
		default:
			return false;
		}
	}

	private static bool IsExistingQuestionnaireBackedConstruct(string construct)
	{
		switch (construct)
		{
		case "cognitive_load":
		case "safety_comfort":
		case "immersion":
		case "usability":
		case "user_experience":
		case "physical_demand":
		case "presence_social":
		case "motion_sickness":
		case "collaboration_quality":
		case "continuance_intention":
		case "presence_combined":
		case "simulator_realism":
		case "presence":
		case "presence_spatial":
		case "presence_self":
		case "embodiment":
		case "virtual_therapist_alliance":
		case "trust":
		case "stress":
			return true;
		default:
			return false;
		}
	}

	private static bool ContainsVagueInstrumentLabel(params string[] values)
	{
		return ContainsAny(string.Join(" ", values ?? Array.Empty<string>()).ToLowerInvariant(), "sus-like", "vrsuq-like", "presence-like", "multimodal-presence-like", "nasa-tlx-like", "ipq-like", "veq-like", "vtas-like", "vrsq-ssq-like", "ssq-like");
	}

	private static bool ResearcherRequestedCustomInstrument(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (ResearcherRejectedCustomInstrument(text2))
		{
			return false;
		}
		return ContainsAny(text2, "custom questionnaire", "custom survey", "custom items", "self-made questionnaire", "make my own questionnaire", "create a questionnaire", "design a questionnaire", "new questionnaire", "original questionnaire", "not standardized", "no standard questionnaire", "do not use standard", "don't use standard", "自定义问卷", "自制问卷", "自己做问卷", "自己制作问卷", "设计一个问卷", "编一个问卷", "创建一个问卷", "你自己做一个问卷", "不用标准问卷", "不要标准问卷", "不追求标准化", "快速做 pilot", "做 pilot", "生成一个短的", "生成一个短问卷", "短的 vr 体验问卷");
	}

	private static bool ResearcherRejectedCustomInstrument(string? text)
	{
		return ContainsAny((text ?? "").ToLowerInvariant(), "do not make custom", "don't make custom", "do not create custom", "don't create custom", "no custom", "not custom", "without custom", "standard only", "only standard", "only standardized", "validated only", "existing standard", "existing questionnaire", "不要自制", "不要自定义", "不用自制", "不用自定义", "请不要帮我自制", "别自制", "别自定义", "只推荐已有标准问卷", "只用已有标准问卷", "只推荐标准问卷", "只用标准问卷", "已有标准问卷", "标准问卷优先");
	}

	private static bool LooksLikeCompoundConstruct(string? construct)
	{
		return ContainsAny((construct ?? "").Trim().ToLowerInvariant(), ",", ";", "/", "|", "+", "，", "、", " and ", " 和 ");
	}

	private static List<string> SplitCompoundConstructs(string? construct)
	{
		List<string> list = new List<string>();
		string text = (construct ?? "").Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return list;
		}
		text = Regex.Replace(text, "\\band\\b", ",", RegexOptions.IgnoreCase);
		text = text.Replace("，", ",").Replace("、", ",").Replace(";", ",")
			.Replace("/", ",")
			.Replace("|", ",")
			.Replace("+", ",")
			.Replace(" 和 ", ",");
		string[] array = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = NormalizeConstructName(array[i]);
			if (string.IsNullOrWhiteSpace(text2))
			{
				continue;
			}
			bool flag = false;
			foreach (string item in list)
			{
				if (QuestionnaireTaskKey(item).Equals(QuestionnaireTaskKey(text2), StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				list.Add(text2);
			}
		}
		return list;
	}

	private static string NormalizeConstructName(string value)
	{
		string text = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), "[\\[\\]\"'`{}]", "");
		text = text.Replace("-", "_").Replace(" ", "_");
		switch (text)
		{
		case "cognitive_load":
		case "workload":
		case "nasa_tlx":
		case "cognitive":
		case "nasa":
		case "tlx":
			return "cognitive_load";
		case "usability":
		case "sus":
		case "usable":
		case "system_usability":
			return "usability";
		case "ueq":
		case "user_experience":
		case "vrsuq":
		case "vr_user_experience":
		case "ux":
			return "user_experience";
		case "exertion":
		case "borg_rpe":
		case "borg":
		case "rpe":
		case "physical_demand":
		case "physical_burden":
		case "perceived_exertion":
		case "physical_workload":
		case "fatigue":
			return "physical_demand";
		case "safety_comfort":
		case "pain":
		case "safety":
		case "comfort":
		case "rehabilitation_safety":
		case "rehab_safety":
			return "safety_comfort";
		case "adoption":
		case "adherence":
		case "tam":
		case "intention_to_use":
		case "utaut":
		case "continuance_intention":
		case "continuance":
		case "continued_use":
		case "future_use":
		case "acceptance":
			return "continuance_intention";
		case "presence":
		case "immersion_presence":
		case "combined_presence":
		case "presence_combined":
			return "presence_combined";
		case "ipq":
		case "spatial_presence":
		case "presence_spatial":
			return "presence_spatial";
		case "social_presence":
		case "presence_social":
		case "co_presence":
		case "copresence":
			return "presence_social";
		case "collaboration_quality":
		case "communication_quality":
		case "collaboration":
		case "communication":
			return "collaboration_quality";
		case "avatar_presence":
		case "self_presence":
		case "presence_self":
			return "presence_self";
		case "vrsq":
		case "ssq":
		case "motion_sickness":
		case "simulator_sickness":
		case "cybersickness":
			return "motion_sickness";
		case "fidelity":
		case "simulation_realism":
		case "simulator_realism":
		case "realism":
			return "simulator_realism";
		case "immersion":
		case "ieq":
			return "immersion";
		case "body_ownership":
		case "veq":
		case "embodiment":
			return "embodiment";
		case "vtas":
		case "therapist_alliance":
		case "virtual_coach":
		case "virtual_therapist_alliance":
			return "virtual_therapist_alliance";
		case "trust":
			return "trust";
		case "pressure":
		case "stress":
			return "stress";
		case "custom":
			return "custom";
		default:
			if (text.Contains("sus", StringComparison.OrdinalIgnoreCase))
			{
				return "usability";
			}
			if (text.Contains("ueq", StringComparison.OrdinalIgnoreCase) || text.Contains("experience", StringComparison.OrdinalIgnoreCase))
			{
				return "user_experience";
			}
			if (text.Contains("borg", StringComparison.OrdinalIgnoreCase) || text.Contains("rpe", StringComparison.OrdinalIgnoreCase) || text.Contains("physical", StringComparison.OrdinalIgnoreCase))
			{
				return "physical_demand";
			}
			if (text.Contains("safety", StringComparison.OrdinalIgnoreCase) || text.Contains("comfort", StringComparison.OrdinalIgnoreCase) || text.Contains("pain", StringComparison.OrdinalIgnoreCase))
			{
				return "safety_comfort";
			}
			if (text.Contains("collaboration", StringComparison.OrdinalIgnoreCase) || text.Contains("communication", StringComparison.OrdinalIgnoreCase))
			{
				return "collaboration_quality";
			}
			if (text.Contains("continue", StringComparison.OrdinalIgnoreCase) || text.Contains("intention", StringComparison.OrdinalIgnoreCase) || text.Contains("adoption", StringComparison.OrdinalIgnoreCase) || text.Contains("acceptance", StringComparison.OrdinalIgnoreCase))
			{
				return "continuance_intention";
			}
			return "";
		}
	}

	private static string QuestionnaireTaskKey(string construct)
	{
		string text = (construct ?? "").Trim().ToLowerInvariant();
		if (!(text == "presence"))
		{
			if (text == "embodiment")
			{
				return "presence_self";
			}
			return text;
		}
		return "presence_combined";
	}

	private static List<string> BuildWarningsForTaskConfigs(List<StudyConfig> taskConfigs)
	{
		List<string> list = new List<string>();
		foreach (StudyConfig taskConfig in taskConfigs)
		{
			foreach (string item in BuildWarnings(taskConfig))
			{
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	private static List<string> BuildContextWarnings(string userRequest)
	{
		List<string> list = new List<string>();
		if (ContainsResponseDetectionContext(userRequest) || ContainsQuestionnaireSystemContext(userRequest))
		{
			list.Add("Answer detection should be evaluated as an objective system metric, not as a participant questionnaire construct. Log completion rate, detection accuracy, false positives, false negatives, missing-response rate, and detection latency.");
		}
		if (ContainsPerformanceContext(userRequest) || ContainsTaskVariationContext(userRequest))
		{
			list.Add("For comparisons across tasks, log objective Unity task metrics such as accuracy, completion, score, breathing-training outcome, or completion time. PAXSM will keep reaction-time and raw stage-event logs, but task-specific performance scoring still needs Unity-side logging.");
		}
		if (ContainsXrOrImmersiveSystemContext(userRequest) && !ContainsMotionSicknessQuestionnaireContext(userRequest))
		{
			list.Add("For XR/VR studies, consider adding FMS, SSQ, or VRSQ if participants wear an HMD, experience motion, or may have cybersickness/discomfort. Do not add it automatically if discomfort is outside the research scope.");
		}
		if ((ContainsSelfPresenceQuestionnaireContext(userRequest) || ContainsAny(userRequest.ToLowerInvariant(), "avatar", "virtual hands", "hand tracking", "body tracking")) && !ContainsAny(userRequest.ToLowerInvariant(), "embodiment", "agency", "body ownership"))
		{
			list.Add("If avatars, virtual hands, or body tracking are central, add an embodiment/agency measure rather than relying only on general presence.");
		}
		return list;
	}

	private static string BuildTaskSetSummary(string userRequest, string fallbackSummary, List<StudyConfig> taskConfigs)
	{
		if (taskConfigs.Count <= 1)
		{
			if (taskConfigs.Count == 1)
			{
				return BuildSingleTaskSummary(taskConfigs[0], fallbackSummary);
			}
			if (!string.IsNullOrWhiteSpace(fallbackSummary))
			{
				return fallbackSummary;
			}
			return "I recommend one questionnaire task for this setup.";
		}
		if (ShouldUseStructuredModuleSummary(userRequest))
		{
			return BuildStructuredModuleSummary(userRequest, taskConfigs);
		}
		List<string> list = new List<string>();
		foreach (StudyConfig taskConfig in taskConfigs)
		{
			list.Add(IsDirectQuestionnaireConfigurationRequest(userRequest) ? DirectConfiguredTaskLabel(taskConfig) : QuestionnaireTaskLabel(taskConfig));
		}
		if (IsDirectQuestionnaireConfigurationRequest(userRequest))
		{
			return "Configured the requested questionnaire task(s): " + JoinHumanReadable(list) + ". This is only a preview; files will be written after Confirm and apply.";
		}
		string text = "I recommend separate questionnaire tasks for this experiment: " + JoinHumanReadable(list) + ".";
		if (ContainsResponseDetectionContext(userRequest) || ContainsQuestionnaireSystemContext(userRequest))
		{
			text += " Treat answer detection as objective system validation, not as a questionnaire construct.";
		}
		if (ContainsPerformanceContext(userRequest) || ContainsTaskVariationContext(userRequest))
		{
			text += " For task-to-task performance comparisons, use objective Unity logging rather than a questionnaire item.";
		}
		return text;
	}

	private static bool ShouldUseStructuredModuleSummary(string userRequest)
	{
		if (!ContainsPhysicalDemandQuestionnaireContext(userRequest) && !ContainsSafetyComfortQuestionnaireContext(userRequest) && !ContainsContinuanceIntentionQuestionnaireContext(userRequest))
		{
			return ContainsAny(userRequest.ToLowerInvariant(), "rehab", "rehabilitation", "康复");
		}
		return true;
	}

	private static string BuildStructuredModuleSummary(string userRequest, List<StudyConfig> taskConfigs)
	{
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = ContainsAny(userRequest.ToLowerInvariant(), "rehab", "rehabilitation", "康复");
		stringBuilder.AppendLine(flag ? BuildRehabilitationSummaryOpening(userRequest) : "For this study, I recommend a questionnaire setup with core modules and conditional modules.");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Core:");
		int num = 1;
		foreach (StudyConfig taskConfig in taskConfigs)
		{
			ApplyInstrumentMetadata(taskConfig);
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
			handler.AppendFormatted(num);
			handler.AppendLiteral(". ");
			handler.AppendFormatted(StructuredModuleTitle(taskConfig.construct));
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(38, 2, stringBuilder2);
			handler.AppendLiteral("   Primary recommended instrument: ");
			handler.AppendFormatted(taskConfig.instrumentName);
			handler.AppendLiteral(" [");
			handler.AppendFormatted(taskConfig.instrumentStatus);
			handler.AppendLiteral("]");
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(35, 1, stringBuilder2);
			handler.AppendLiteral("   Existing questionnaire options: ");
			handler.AppendFormatted(taskConfig.recommendedStandardInstrument);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
			handler.AppendLiteral("   Purpose: ");
			handler.AppendFormatted(taskConfig.recommendationRationale);
			stringBuilder6.AppendLine(ref handler);
			num++;
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Conditional modules:");
		stringBuilder.AppendLine("- Add FMS or SSQ/VRSQ if the system uses an HMD, involves motion, or may cause cybersickness/discomfort.");
		stringBuilder.AppendLine("- Add embodiment/agency only if virtual hands, avatars, body tracking, or movement ownership are central to the study.");
		stringBuilder.AppendLine("- Add trust only if the system includes AI coaching, automated feedback, clinical recommendations, or autonomous guidance.");
		stringBuilder.AppendLine("- Add stress/anxiety only if emotional stress, fear of movement, pain anxiety, or clinical anxiety is part of the research question.");
		return stringBuilder.ToString().TrimEnd();
	}

	private static string BuildRehabilitationSummaryOpening(string userRequest)
	{
		List<string> list = new List<string> { "experience", "physical demand", "safety/comfort", "willingness to continue" };
		if (ContainsTaskDifficultyQuestionnaireContext(userRequest))
		{
			list.Add("cognitive workload");
		}
		return "For this VR rehabilitation training system, I recommend a questionnaire setup focused on " + JoinHumanReadable(list) + ".";
	}

	private static string StructuredModuleTitle(string construct)
	{
		switch (construct)
		{
		case "usability":
			return "Usability / interaction experience";
		case "user_experience":
			return "Use experience / XR experience";
		case "physical_demand":
			return "Physical demand / perceived exertion";
		case "safety_comfort":
			return "Safety / comfort";
		case "continuance_intention":
			return "Willingness to continue / adoption intention";
		case "cognitive_load":
			return "Cognitive workload";
		case "motion_sickness":
			return "Cybersickness / discomfort";
		case "presence_combined":
		case "presence":
			return "Presence / immersion";
		case "collaboration_quality":
			return "Collaboration / communication quality";
		case "presence_self":
		case "embodiment":
			return "Embodiment / agency";
		case "trust":
			return "Trust";
		case "stress":
			return "Stress / anxiety";
		default:
			return construct;
		}
	}

	private static string BuildSingleTaskSummary(StudyConfig config, string fallbackSummary)
	{
		ApplyInstrumentMetadata(config);
		if (IsDirectQuestionnaireConfigurationRequest(config.naturalLanguageRequest) || ContainsResearcherImportedStandardQuestionnaire(config.naturalLanguageRequest))
		{
			string text;
			switch (config.construct)
			{
			case "cognitive_load":
				if (ContainsSingleItemWorkloadRequest(config.naturalLanguageRequest))
				{
					text = "Configured a single-item cognitive workload rating: How mentally demanding was the task? This is not full NASA-TLX; it measures mental demand as one 21-point slider item.";
					break;
				}
				if (ContainsExactNasaTlxRequest(config.naturalLanguageRequest))
				{
					text = "Configured NASA-TLX workload ratings with the six workload dimensions on 21-point slider responses. If pairwise weighting is omitted, report it as Raw NASA-TLX / unweighted NASA-TLX.";
					break;
				}
				goto default;
			case "usability":
				if (ContainsExactSusRequest(config.naturalLanguageRequest))
				{
					text = "Configured SUS as a 10-item, 5-point card questionnaire. Preserve reverse scoring and compute the standard 0-100 SUS total score before reporting it.";
					break;
				}
				goto default;
			case "user_experience":
				if (ContainsExactUeqSRequest(config.naturalLanguageRequest))
				{
					text = "Configured a UEQ-S deployment task. Import and verify the full UEQ-S wording, bipolar response scale, and scoring before reporting it as standardized UEQ-S.";
					break;
				}
				goto default;
			case "motion_sickness":
				if (ContainsFullSsqRequest(config.naturalLanguageRequest) || ContainsExactSsqRequest(config.naturalLanguageRequest))
				{
					text = "Configured the full Simulator Sickness Questionnaire (SSQ): 16 symptom items with 4-point card responses (None, Slight, Moderate, Severe). Preserve the original SSQ symptom set and scoring before reporting standardized SSQ results.";
					break;
				}
				goto default;
			default:
				text = "Configured the requested questionnaire task: " + DirectConfiguredTaskLabel(config) + ".";
				break;
			}
			return text + " This is only a preview; files will be written after Confirm and apply.";
		}
		if (config.construct.Equals("motion_sickness", StringComparison.OrdinalIgnoreCase) && (ContainsFullSsqRequest(config.naturalLanguageRequest) || ContainsExactSsqRequest(config.naturalLanguageRequest)))
		{
			return "Configured the full Simulator Sickness Questionnaire (SSQ): 16 symptom items with 4-point card responses (None, Slight, Moderate, Severe). This is treated as a standardized questionnaire when the original SSQ items, response format, and scoring are preserved.";
		}
		string text2 = (string.IsNullOrWhiteSpace(config.recommendationRole) ? "questionnaire module" : config.recommendationRole.Trim());
		string value = (text2.Equals("Core", StringComparison.OrdinalIgnoreCase) ? "a core questionnaire module" : (text2.StartsWith("Conditional", StringComparison.OrdinalIgnoreCase) ? ("a " + text2.ToLowerInvariant()) : ("a " + text2.ToLowerInvariant())));
		string value2 = (string.IsNullOrWhiteSpace(config.instrumentName) ? config.construct.Trim() : config.instrumentName.Trim());
		string value3 = (string.IsNullOrWhiteSpace(config.instrumentStatus) ? "custom/adapted" : config.instrumentStatus.Trim());
		string value4 = (string.IsNullOrWhiteSpace(config.recommendedStandardInstrument) ? "Choose a validated scale if this construct is central to the paper claim." : config.recommendedStandardInstrument.Trim());
		string value5 = (string.IsNullOrWhiteSpace(config.recommendationRationale) ? fallbackSummary : config.recommendationRationale.Trim());
		return $"I recommend {value} using {value2}. Instrument status: {value3}. Existing questionnaire option(s) to cite or use include {value4}. PAXSM will configure {config.scale}-point {config.responseMode} responses. {value5}";
	}

	private static string QuestionnaireTaskLabel(StudyConfig config)
	{
		ApplyInstrumentMetadata(config);
		string value = (string.IsNullOrWhiteSpace(config.recommendationRole) ? "Questionnaire" : config.recommendationRole.Trim());
		string value2 = (string.IsNullOrWhiteSpace(config.instrumentName) ? config.construct.Trim() : config.instrumentName.Trim());
		string value3 = (string.IsNullOrWhiteSpace(config.instrumentStatus) ? "custom/adapted" : config.instrumentStatus.Trim());
		string value4 = (string.IsNullOrWhiteSpace(config.recommendedStandardInstrument) ? "" : ("; standard option: " + config.recommendedStandardInstrument.Trim()));
		return $"{value}: {value2} ({value3}{value4}; {config.scale}-point {config.responseMode})";
	}

	private static string DirectConfiguredTaskLabel(StudyConfig config)
	{
		ApplyInstrumentMetadata(config);
		string text = config.naturalLanguageRequest ?? "";
		string text2;
		switch (config.construct)
		{
		case "motion_sickness":
			if (ContainsExactSsqRequest(text) || ContainsFullSsqRequest(text))
			{
				text2 = "SSQ full";
				break;
			}
			goto default;
		case "usability":
			if (ContainsExactSusRequest(text))
			{
				text2 = "SUS";
				break;
			}
			goto default;
		case "cognitive_load":
			if (ContainsExactNasaTlxRequest(text))
			{
				text2 = "NASA-TLX";
				break;
			}
			goto default;
		default:
			text2 = (string.IsNullOrWhiteSpace(config.instrumentName) ? config.construct : config.instrumentName);
			break;
		}
		string value = text2;
		string value2 = (string.IsNullOrWhiteSpace(config.instrumentStatus) ? "configured" : config.instrumentStatus.Trim());
		return $"{value} ({value2}; {config.scale}-point {config.responseMode})";
	}

	private static string JoinHumanReadable(List<string> values)
	{
		if (values.Count == 0)
		{
			return "";
		}
		if (values.Count == 1)
		{
			return values[0];
		}
		if (values.Count == 2)
		{
			return values[0] + " and " + values[1];
		}
		return string.Join(", ", values.GetRange(0, values.Count - 1)) + ", and " + values[values.Count - 1];
	}

	private static AgentResult BuildAgentResultFromLlmContent(string content, string userRequest, string source)
	{
		AgentResult agentResult = TryBuildContextRecommendation(userRequest, source + " + curated questionnaire recommendation");
		if (agentResult != null)
		{
			return agentResult;
		}
		if (!TryExtractJsonObject(content, out string json))
		{
			AgentResult agentResult2 = TryBuildContextRecommendation(userRequest, source + " + questionnaire recommendation");
			if (agentResult2 != null)
			{
				return agentResult2;
			}
			return BuildVagueRequestClarification(source);
		}
		JsonDocument jsonDocument;
		try
		{
			jsonDocument = JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			AgentResult agentResult3 = TryBuildContextRecommendation(userRequest, source + " + questionnaire recommendation");
			if (agentResult3 != null)
			{
				return agentResult3;
			}
			return BuildVagueRequestClarification(source);
		}
		using (jsonDocument)
		{
			bool flag = jsonDocument.RootElement.ValueKind == JsonValueKind.Object && (HasJsonPropertyIgnoreCase(jsonDocument.RootElement, "needsClarification") || HasJsonPropertyIgnoreCase(jsonDocument.RootElement, "config"));
			LlmDecision llmDecision = (flag ? JsonSerializer.Deserialize<LlmDecision>(json, JsonOptions) : null);
			if (llmDecision != null)
			{
				LlmDecision llmDecision2 = llmDecision;
				if (llmDecision2.Questions == null)
				{
					List<string> list = (llmDecision2.Questions = new List<string>());
				}
				llmDecision2 = llmDecision;
				if (llmDecision2.Warnings == null)
				{
					List<string> list = (llmDecision2.Warnings = new List<string>());
				}
			}
			if (llmDecision != null && llmDecision.NeedsClarification)
			{
				AgentResult agentResult4 = TryBuildContextRecommendation(userRequest, source + " + questionnaire recommendation");
				if (agentResult4 != null)
				{
					foreach (string warning in llmDecision.Warnings)
					{
						if (!string.IsNullOrWhiteSpace(warning) && !IsNoisyModelWarning(warning) && !agentResult4.Warnings.Contains(warning))
						{
							agentResult4.Warnings.Add(warning);
						}
					}
					return agentResult4;
				}
				List<string> list4 = RemoveInternalClarificationQuestions(llmDecision.Questions);
				if (list4.Count == 0)
				{
					list4.Add("请只补充问卷/构念、量表点数或作答方式。也可以直接说“你建议一下”，让我按推荐问卷设置生成。");
					llmDecision.Warnings.Add("The model asked for internal config fields; PAXSM fills those automatically.");
				}
				return new AgentResult
				{
					Source = source,
					Summary = (string.IsNullOrWhiteSpace(llmDecision.Summary) ? "I need a few details before I can safely configure the study." : llmDecision.Summary),
					NeedsClarification = true,
					Questions = list4,
					Warnings = llmDecision.Warnings
				};
			}
			StudyConfig studyConfig = null;
			if (flag)
			{
				JsonElement? jsonElement = llmDecision?.Config;
				if (jsonElement.HasValue)
				{
					JsonElement valueOrDefault = jsonElement.GetValueOrDefault();
					if (valueOrDefault.ValueKind == JsonValueKind.Object)
					{
						studyConfig = DeserializeStudyConfigFlexible(valueOrDefault.GetRawText());
						goto IL_0282;
					}
				}
			}
			if (!flag)
			{
				studyConfig = DeserializeStudyConfigFlexible(json);
			}
			goto IL_0282;
			IL_0282:
			if (studyConfig == null)
			{
				AgentResult agentResult5 = TryBuildContextRecommendation(userRequest, source + " + questionnaire recommendation");
				if (agentResult5 != null)
				{
					return agentResult5;
				}
				return BuildVagueRequestClarification(source);
			}
			NormalizeConfig(studyConfig, userRequest);
			List<StudyConfig> list5 = BuildQuestionnaireTaskConfigs(userRequest, studyConfig);
			List<string> first = MergeWarnings(llmDecision?.Warnings, BuildWarningsForTaskConfigs(list5));
			first = MergeWarnings(first, BuildContextWarnings(userRequest));
			return new AgentResult
			{
				Config = ((list5.Count > 0) ? list5[0] : studyConfig),
				Source = source,
				Summary = BuildTaskSetSummary(userRequest, studyConfig.generatedSummary, list5),
				Warnings = first,
				TaskConfigs = list5
			};
		}
	}

	private static AgentResult BuildVagueRequestClarification(string source)
	{
		return new AgentResult
		{
			Source = source,
			Summary = "我还需要先了解你这次实验想评估什么，暂时不会写入 Unity 配置。",
			NeedsClarification = true,
			Questions = new List<string> { "你主要想测什么？例如：系统是否好用、沉浸感/临场感、认知负荷、压力/放松、信任，或任务表现。", "你想让我直接推荐问卷，还是你已经知道要用哪个问卷/量表？", "如果你知道偏好，请告诉我量表点数和作答方式，例如 5 点 card、7 点 card、21 点 slider；如果不知道，可以说“你建议”。" },
			Warnings = new List<string>()
		};
	}

	private static bool HasJsonPropertyIgnoreCase(JsonElement element, string name)
	{
		foreach (JsonProperty item in element.EnumerateObject())
		{
			if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static StudyConfig? DeserializeStudyConfigFlexible(string json)
	{
		if (!(JsonNode.Parse(json) is JsonObject jsonObject))
		{
			return null;
		}
		NormalizeConfigAliases(jsonObject);
		NormalizeStringNodes(jsonObject);
		NormalizeStringListNode(jsonObject, "conditions");
		NormalizeScaleNode(jsonObject);
		NormalizeIntNode(jsonObject, "participantNumber", 1);
		NormalizeIntNode(jsonObject, "sessionNumber", 1);
		NormalizeIntNode(jsonObject, "conditionIndex", 1);
		NormalizeBoolNode(jsonObject, "randomizeQuestions", fallback: false);
		NormalizeBoolNode(jsonObject, "exportMergedCsv", fallback: true);
		NormalizeBoolNode(jsonObject, "exportRawStageEvents", fallback: true);
		NormalizeBoolNode(jsonObject, "exportOnQuit", fallback: true);
		NormalizeBoolNode(jsonObject, "preventOverwrite", fallback: true);
		return jsonObject.Deserialize<StudyConfig>(JsonOptions);
	}

	private static void NormalizeStringNodes(JsonObject obj)
	{
		string[] array = new string[21]
		{
			"schemaVersion", "studyName", "studyVersion", "design", "construct", "participantId", "experimenterId", "conditionLabel", "counterbalancingOrder", "questionBankResourcesPath",
			"responseMode", "recommendationRole", "instrumentName", "instrumentStatus", "recommendedStandardInstrument", "recommendationRationale", "outputFolder", "outputSubfolder", "fileNamePrefix", "naturalLanguageRequest",
			"generatedSummary"
		};
		foreach (string text in array)
		{
			if (obj.TryGetPropertyValue(text, out JsonNode jsonNode) && jsonNode != null)
			{
				obj[text] = CoerceNodeToString(jsonNode, text);
			}
		}
	}

	private static string CoerceNodeToString(JsonNode node, string fieldName)
	{
		if (node is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<string>(out string value))
			{
				return value ?? "";
			}
			if (jsonValue.TryGetValue<int>(out var value2))
			{
				return value2.ToString();
			}
			if (jsonValue.TryGetValue<bool>(out var value3))
			{
				if (!value3)
				{
					return "false";
				}
				return "true";
			}
		}
		if (node is JsonArray jsonArray)
		{
			List<string> list = new List<string>();
			foreach (JsonNode item in jsonArray)
			{
				if (item != null)
				{
					string text = CoerceNodeToString(item, fieldName);
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add(text);
					}
				}
			}
			if (!fieldName.Equals("conditionLabel", StringComparison.OrdinalIgnoreCase))
			{
				return string.Join(", ", list);
			}
			return string.Join("_", list);
		}
		if (node is JsonObject jsonObject)
		{
			string[] array = new string[6] { "label", "name", "value", "text", "id", "type" };
			foreach (string propertyName in array)
			{
				if (jsonObject.TryGetPropertyValue(propertyName, out JsonNode jsonNode) && jsonNode != null)
				{
					return CoerceNodeToString(jsonNode, fieldName);
				}
			}
		}
		return node.ToJsonString();
	}

	private static void NormalizeStringListNode(JsonObject obj, string fieldName)
	{
		if (!obj.TryGetPropertyValue(fieldName, out JsonNode jsonNode) || jsonNode == null)
		{
			return;
		}
		List<string> list = new List<string>();
		if (jsonNode is JsonArray jsonArray)
		{
			foreach (JsonNode item in jsonArray)
			{
				if (item != null)
				{
					string text = CoerceNodeToString(item, fieldName);
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add(text);
					}
				}
			}
		}
		else
		{
			string text2 = CoerceNodeToString(jsonNode, fieldName);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				string[] array = Regex.Split(text2, "\\s*(?:/|,|;|\\||\\bvs\\b|\\bversus\\b)\\s*", RegexOptions.IgnoreCase);
				foreach (string text3 in array)
				{
					if (!string.IsNullOrWhiteSpace(text3))
					{
						list.Add(text3.Trim());
					}
				}
			}
		}
		JsonArray jsonArray2 = new JsonArray();
		foreach (string item2 in list)
		{
			jsonArray2.Add(item2);
		}
		obj[fieldName] = jsonArray2;
	}

	private static void NormalizeConfigAliases(JsonObject obj)
	{
		CopyAlias(obj, "studyDesign", "design");
		CopyAlias(obj, "conditionLabels", "conditions");
		CopyAlias(obj, "questionnaireType", "construct");
		CopyAlias(obj, "questionnaire", "construct");
		CopyAlias(obj, "answerMode", "responseMode");
		CopyAlias(obj, "scalePoints", "scale");
		CopyAlias(obj, "likertPoints", "scale");
	}

	private static void CopyAlias(JsonObject obj, string alias, string target)
	{
		if (!obj.ContainsKey(target) && obj.TryGetPropertyValue(alias, out JsonNode jsonNode) && jsonNode != null)
		{
			obj[target] = jsonNode.DeepClone();
		}
	}

	private static void NormalizeScaleNode(JsonObject obj)
	{
		if (!obj.TryGetPropertyValue("scale", out JsonNode jsonNode) || jsonNode == null)
		{
			return;
		}
		if (jsonNode is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				obj["scale"] = value;
				return;
			}
			if (jsonValue.TryGetValue<string>(out string value2))
			{
				obj["scale"] = ExtractScaleFromText(value2);
				return;
			}
		}
		obj["scale"] = ExtractScaleFromText(jsonNode.ToJsonString());
	}

	private static void NormalizeIntNode(JsonObject obj, string fieldName, int fallback)
	{
		if (!obj.TryGetPropertyValue(fieldName, out JsonNode jsonNode) || jsonNode == null)
		{
			return;
		}
		if (jsonNode is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<int>(out var value))
			{
				obj[fieldName] = value;
				return;
			}
			if (jsonValue.TryGetValue<string>(out string value2))
			{
				Match match = Regex.Match(value2 ?? "", "\\d+");
				obj[fieldName] = ((match.Success && int.TryParse(match.Value, out var result)) ? result : fallback);
				return;
			}
		}
		obj[fieldName] = fallback;
	}

	private static void NormalizeBoolNode(JsonObject obj, string fieldName, bool fallback)
	{
		if (!obj.TryGetPropertyValue(fieldName, out JsonNode jsonNode) || jsonNode == null)
		{
			return;
		}
		if (jsonNode is JsonValue jsonValue)
		{
			if (jsonValue.TryGetValue<bool>(out var value))
			{
				obj[fieldName] = value;
				return;
			}
			if (jsonValue.TryGetValue<string>(out string value2))
			{
				string text = (value2 ?? "").Trim().ToLowerInvariant();
				if (ContainsAny(text, "true", "yes", "enable", "enabled", "on", "1"))
				{
					obj[fieldName] = true;
					return;
				}
				if (ContainsAny(text, "false", "no", "disable", "disabled", "off", "0"))
				{
					obj[fieldName] = false;
					return;
				}
			}
		}
		obj[fieldName] = fallback;
	}

	private static int ExtractScaleFromText(string? text)
	{
		string text2 = (text ?? "").ToLowerInvariant();
		if (ContainsAny(text2, "21", "twenty-one", "nasa-tlx", "tlx"))
		{
			return 21;
		}
		if (ContainsAny(text2, "7", "seven"))
		{
			return 7;
		}
		if (ContainsAny(text2, "5", "five", "sus"))
		{
			return 5;
		}
		return 21;
	}

	private static List<string> RemoveInternalClarificationQuestions(List<string> questions)
	{
		List<string> list = new List<string>();
		foreach (string question in questions)
		{
			if (!string.IsNullOrWhiteSpace(question) && !IsInternalClarificationQuestion(question))
			{
				list.Add(question);
			}
		}
		return list;
	}

	private static bool IsInternalClarificationQuestion(string question)
	{
		return ContainsAny(question, "schemaVersion", "studyName", "studyVersion", "participantId", "participantNumber", "sessionNumber", "experimenterId", "questionBankResourcesPath", "outputFolder", "outputSubfolder", "fileNamePrefix", "resource path", "resources path", "study name", "study version", "研究名称", "研究版本", "资源路径", "量表资源路径", "输出路径", "参与者编号", "session number");
	}

	public string ToJson(StudyConfig config)
	{
		return JsonSerializer.Serialize(config, JsonOptions);
	}

	public LikertQuestionBank BuildQuestionBank(StudyConfig config)
	{
		return (config.construct ?? "").Trim().ToLowerInvariant() switch
		{
			"cognitive_load" => BuildCognitiveLoadBank(config), 
			"usability" => BuildUsabilityBank(config), 
			"user_experience" => BuildUserExperienceBank(config), 
			"physical_demand" => BuildPhysicalDemandBank(config), 
			"safety_comfort" => BuildSafetyComfortBank(config), 
			"continuance_intention" => BuildContinuanceIntentionBank(config), 
			"presence" => BuildPresenceBank(config), 
			"presence_spatial" => BuildSpatialPresenceBank(config), 
			"presence_social" => BuildSocialPresenceBank(config), 
			"collaboration_quality" => BuildCollaborationQualityBank(config), 
			"presence_self" => BuildSelfPresenceBank(config), 
			"presence_combined" => BuildCombinedPresenceBank(config), 
			"motion_sickness" => BuildMotionSicknessBank(config), 
			"simulator_realism" => BuildSimulatorRealismBank(config), 
			"immersion" => BuildImmersionBank(config), 
			"embodiment" => BuildSelfPresenceBank(config), 
			"virtual_therapist_alliance" => BuildVirtualTherapistAllianceBank(config), 
			"trust" => BuildTrustBank(config), 
			"stress" => BuildStressBank(config), 
			_ => BuildCustomBank(config), 
		};
	}

	public void RefreshInstrumentMetadata(StudyConfig config)
	{
		ApplyInstrumentMetadata(config, force: true);
	}

	private static LikertQuestionBank BuildCognitiveLoadBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		if (ContainsSingleItemWorkloadRequest(config.naturalLanguageRequest))
		{
			return new LikertQuestionBank
			{
				version = 1,
				scale = Math.Max(2, config.scale),
				default_mode = text,
				labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
				items = new List<LikertItem>
				{
					new LikertItem
					{
						id = "mental_demand_single",
						stem = "How mentally demanding was the task?",
						mode = text
					}
				}
			};
		}
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "mentaldemand",
					stem = "How mentally demanding was the task?",
					mode = text
				},
				new LikertItem
				{
					id = "physicaldemand",
					stem = "How physically demanding was the task?",
					mode = text
				},
				new LikertItem
				{
					id = "temporal",
					stem = "How hurried or rushed was the pace of the task?",
					mode = text
				},
				new LikertItem
				{
					id = "performance",
					stem = "How successful were you in accomplishing what you were asked to do?",
					mode = text
				},
				new LikertItem
				{
					id = "effort",
					stem = "How hard did you have to work to accomplish your level of performance?",
					mode = text
				},
				new LikertItem
				{
					id = "frustration",
					stem = "How insecure, discouraged, irritated, stressed, or annoyed were you?",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildUsabilityBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "sus_01_frequency",
					stem = "I think that I would like to use this system frequently.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_02_complexity",
					stem = "I found the system unnecessarily complex.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_03_ease",
					stem = "I thought the system was easy to use.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_04_support",
					stem = "I think that I would need the support of a technical person to be able to use this system.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_05_integration",
					stem = "I found the various functions in this system were well integrated.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_06_inconsistency",
					stem = "I thought there was too much inconsistency in this system.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_07_learnability",
					stem = "I would imagine that most people would learn to use this system very quickly.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_08_cumbersome",
					stem = "I found the system very cumbersome to use.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_09_confidence",
					stem = "I felt very confident using the system.",
					mode = text
				},
				new LikertItem
				{
					id = "sus_10_training",
					stem = "I needed to learn a lot of things before I could get going with this system.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildPresenceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "presence_spatial",
					stem = "I felt physically present in the virtual environment.",
					mode = text
				},
				new LikertItem
				{
					id = "presence_involvement",
					stem = "I felt involved in the virtual task.",
					mode = text
				},
				new LikertItem
				{
					id = "presence_realism",
					stem = "The virtual environment felt realistic.",
					mode = text
				},
				new LikertItem
				{
					id = "presence_attention",
					stem = "My attention was focused on the virtual environment.",
					mode = text
				},
				new LikertItem
				{
					id = "presence_interaction",
					stem = "My interactions in the virtual environment felt natural.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildSpatialPresenceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "spatial_presence_being_there",
					stem = "I felt as if I was really inside the virtual environment.",
					mode = text
				},
				new LikertItem
				{
					id = "spatial_presence_place",
					stem = "The virtual environment felt like a place I visited rather than something I watched.",
					mode = text
				},
				new LikertItem
				{
					id = "spatial_presence_surroundings",
					stem = "I paid more attention to the virtual surroundings than to the real room.",
					mode = text
				},
				new LikertItem
				{
					id = "spatial_presence_layout",
					stem = "The spatial layout of the virtual environment felt consistent and believable.",
					mode = text
				},
				new LikertItem
				{
					id = "spatial_presence_interaction",
					stem = "I felt able to act within the virtual environment naturally.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildSocialPresenceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "social_presence_together",
					stem = "I felt as if I was together with the other person or agent in the virtual environment.",
					mode = text
				},
				new LikertItem
				{
					id = "social_presence_connected",
					stem = "I felt socially connected to the other person or agent.",
					mode = text
				},
				new LikertItem
				{
					id = "social_presence_responsive",
					stem = "The other person or agent responded to me in a believable way.",
					mode = text
				},
				new LikertItem
				{
					id = "social_presence_attention",
					stem = "I felt that the other person or agent was aware of me.",
					mode = text
				},
				new LikertItem
				{
					id = "social_presence_interaction",
					stem = "The social interaction in the virtual environment felt natural.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildCollaborationQualityBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "collab_communication_clear",
					stem = "Communication with the other participant was clear.",
					mode = text
				},
				new LikertItem
				{
					id = "collab_coordination",
					stem = "We were able to coordinate our actions smoothly.",
					mode = text
				},
				new LikertItem
				{
					id = "collab_shared_understanding",
					stem = "We had a shared understanding of what to do during the task.",
					mode = text
				},
				new LikertItem
				{
					id = "collab_feedback",
					stem = "The system supported useful feedback between participants.",
					mode = text
				},
				new LikertItem
				{
					id = "collab_overall_quality",
					stem = "Overall, collaboration in the VR system worked well.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildSelfPresenceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "self_presence_body_mine",
					stem = "The virtual body or avatar felt connected to me.",
					mode = text
				},
				new LikertItem
				{
					id = "self_presence_agency",
					stem = "I felt that I could control the virtual body or avatar as intended.",
					mode = text
				},
				new LikertItem
				{
					id = "self_presence_location",
					stem = "I felt located where the virtual body or avatar was located.",
					mode = text
				},
				new LikertItem
				{
					id = "self_presence_movement",
					stem = "The movement of the virtual body or avatar matched my intended actions.",
					mode = text
				},
				new LikertItem
				{
					id = "self_presence_ownership",
					stem = "Parts of the virtual body or avatar felt like they belonged to me.",
					mode = text
				},
				new LikertItem
				{
					id = "self_presence_identity",
					stem = "The virtual representation felt like a convincing representation of myself.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildCombinedPresenceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "combined_presence_spatial",
					stem = "I felt present inside the virtual environment.",
					mode = text
				},
				new LikertItem
				{
					id = "combined_presence_realism",
					stem = "The virtual environment felt believable.",
					mode = text
				},
				new LikertItem
				{
					id = "combined_presence_interaction",
					stem = "My interactions in the virtual environment felt natural.",
					mode = text
				},
				new LikertItem
				{
					id = "combined_presence_social",
					stem = "If other people or agents were present, they felt socially present to me.",
					mode = text
				},
				new LikertItem
				{
					id = "combined_presence_self",
					stem = "If I had a virtual body or avatar, it felt connected to my actions.",
					mode = text
				},
				new LikertItem
				{
					id = "combined_presence_attention",
					stem = "My attention was focused on the virtual experience.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildUserExperienceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		if (ContainsResearcherSuppliedCustomItems(config.naturalLanguageRequest))
		{
			return new LikertQuestionBank
			{
				version = 1,
				scale = Math.Max(2, config.scale),
				default_mode = text,
				labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
				items = new List<LikertItem>
				{
					new LikertItem
					{
						id = "supplied_satisfaction",
						stem = "满意",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_enjoyment",
						stem = "愉快",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_engagement",
						stem = "投入",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_interaction_clarity",
						stem = "交互清晰",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_feedback_helpful",
						stem = "反馈有帮助",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_hardware_comfort",
						stem = "硬件舒适",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_av_quality",
						stem = "视听质量好",
						mode = text
					},
					new LikertItem
					{
						id = "supplied_continue_use",
						stem = "愿意继续使用",
						mode = text
					}
				}
			};
		}
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "ux_overall_satisfaction",
					stem = "Overall, I was satisfied with the virtual experience.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_enjoyment",
					stem = "The experience was enjoyable.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_engagement",
					stem = "The experience kept me engaged.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_interaction",
					stem = "The interactions were clear and easy to understand.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_feedback",
					stem = "The system feedback helped me understand what was happening.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_hardware_comfort",
					stem = "The hardware and controls supported the experience comfortably.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_visual_audio_quality",
					stem = "The visual and audio quality supported the experience.",
					mode = text
				},
				new LikertItem
				{
					id = "ux_continue",
					stem = "I would be willing to continue using this VR experience.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildPhysicalDemandBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "physical_demand_overall",
					stem = "Overall, the rehabilitation movement felt physically demanding.",
					mode = text
				},
				new LikertItem
				{
					id = "physical_demand_upper_limb_effort",
					stem = "My upper limb or arm had to work hard during the training.",
					mode = text
				},
				new LikertItem
				{
					id = "physical_demand_fatigue",
					stem = "I felt physically fatigued after the training.",
					mode = text
				},
				new LikertItem
				{
					id = "physical_demand_muscle_strain",
					stem = "The movement caused muscle strain or physical discomfort.",
					mode = text
				},
				new LikertItem
				{
					id = "physical_demand_recovery",
					stem = "I needed rest or recovery time after the training.",
					mode = text
				},
				new LikertItem
				{
					id = "physical_demand_manageable",
					stem = "The physical effort required by the training felt manageable.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildSafetyComfortBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "safety_felt_safe",
					stem = "I felt safe while using the rehabilitation system.",
					mode = text
				},
				new LikertItem
				{
					id = "safety_stable",
					stem = "The training movements felt stable and controlled.",
					mode = text
				},
				new LikertItem
				{
					id = "safety_comfortable_range",
					stem = "The required movement range felt comfortable for me.",
					mode = text
				},
				new LikertItem
				{
					id = "safety_low_risk",
					stem = "I did not feel at risk of injury during the training.",
					mode = text
				},
				new LikertItem
				{
					id = "safety_stop_control",
					stem = "I felt I could stop or slow down safely if needed.",
					mode = text
				},
				new LikertItem
				{
					id = "safety_feedback",
					stem = "The system feedback helped me perform the movements safely.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildContinuanceIntentionBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "continuance_continue",
					stem = "I would be willing to continue using this system for rehabilitation training.",
					mode = text
				},
				new LikertItem
				{
					id = "continuance_regular_use",
					stem = "I could imagine using this system regularly as part of rehabilitation.",
					mode = text
				},
				new LikertItem
				{
					id = "continuance_motivation",
					stem = "This system would motivate me to keep doing the training.",
					mode = text
				},
				new LikertItem
				{
					id = "continuance_acceptance",
					stem = "Using this system would be acceptable to me in a rehabilitation routine.",
					mode = text
				},
				new LikertItem
				{
					id = "continuance_benefit",
					stem = "I believe continuing to use this system could support my rehabilitation goals.",
					mode = text
				},
				new LikertItem
				{
					id = "continuance_recommend",
					stem = "I would recommend this rehabilitation system to someone with similar training needs.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildMotionSicknessBank(StudyConfig config)
	{
		if (ContainsFullSsqRequest(config.naturalLanguageRequest) || ContainsExactSsqRequest(config.naturalLanguageRequest) || ResearcherRejectedCustomInstrument(config.naturalLanguageRequest))
		{
			return BuildFullSsqBank(config);
		}
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "None", "Slight", "Moderate", "Severe" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "sickness_nausea",
					stem = "Please rate your nausea during or after the VR experience.",
					mode = text
				},
				new LikertItem
				{
					id = "sickness_dizziness",
					stem = "Please rate your dizziness during or after the VR experience.",
					mode = text
				},
				new LikertItem
				{
					id = "sickness_eye_strain",
					stem = "Please rate your eye strain during or after the VR experience.",
					mode = text
				},
				new LikertItem
				{
					id = "sickness_headache",
					stem = "Please rate your headache during or after the VR experience.",
					mode = text
				},
				new LikertItem
				{
					id = "sickness_disorientation",
					stem = "Please rate your disorientation during or after the VR experience.",
					mode = text
				},
				new LikertItem
				{
					id = "sickness_fatigue",
					stem = "Please rate your fatigue during or after the VR experience.",
					mode = text
				},
				new LikertItem
				{
					id = "sickness_sweating",
					stem = "Please rate any sweating or physical discomfort during or after the VR experience.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildFullSsqBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = 4,
			default_mode = text,
			labels = new List<string> { "None", "Slight", "Moderate", "Severe" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "ssq_general_discomfort",
					stem = "General discomfort",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_fatigue",
					stem = "Fatigue",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_headache",
					stem = "Headache",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_eyestrain",
					stem = "Eyestrain",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_difficulty_focusing",
					stem = "Difficulty focusing",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_increased_salivation",
					stem = "Increased salivation",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_sweating",
					stem = "Sweating",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_nausea",
					stem = "Nausea",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_difficulty_concentrating",
					stem = "Difficulty concentrating",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_fullness_of_head",
					stem = "Fullness of head",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_blurred_vision",
					stem = "Blurred vision",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_dizzy_eyes_open",
					stem = "Dizzy with eyes open",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_dizzy_eyes_closed",
					stem = "Dizzy with eyes closed",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_vertigo",
					stem = "Vertigo",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_stomach_awareness",
					stem = "Stomach awareness",
					mode = text
				},
				new LikertItem
				{
					id = "ssq_burping",
					stem = "Burping",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildSimulatorRealismBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "realism_visual",
					stem = "The visual appearance of the simulation felt realistic.",
					mode = text
				},
				new LikertItem
				{
					id = "realism_interaction",
					stem = "The interactions in the simulation felt realistic.",
					mode = text
				},
				new LikertItem
				{
					id = "realism_response",
					stem = "The simulation responded to my actions in a believable way.",
					mode = text
				},
				new LikertItem
				{
					id = "realism_training",
					stem = "The simulation represented the real task or training situation well.",
					mode = text
				},
				new LikertItem
				{
					id = "realism_sensory",
					stem = "The sensory cues supported the realism of the simulation.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildImmersionBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "immersion_involved",
					stem = "I felt deeply involved in the virtual experience.",
					mode = text
				},
				new LikertItem
				{
					id = "immersion_attention",
					stem = "My attention stayed on the virtual task or experience.",
					mode = text
				},
				new LikertItem
				{
					id = "immersion_time",
					stem = "I lost track of time during the virtual experience.",
					mode = text
				},
				new LikertItem
				{
					id = "immersion_real_world",
					stem = "I was less aware of the real world while using the system.",
					mode = text
				},
				new LikertItem
				{
					id = "immersion_task_focus",
					stem = "I felt absorbed in what I was doing.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildVirtualTherapistAllianceBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "alliance_goals",
					stem = "The virtual therapist or coach helped me work toward clear goals.",
					mode = text
				},
				new LikertItem
				{
					id = "alliance_tasks",
					stem = "The activities suggested by the virtual therapist or coach felt appropriate.",
					mode = text
				},
				new LikertItem
				{
					id = "alliance_support",
					stem = "I felt supported by the virtual therapist or coach.",
					mode = text
				},
				new LikertItem
				{
					id = "alliance_understood",
					stem = "The virtual therapist or coach seemed to understand what I needed.",
					mode = text
				},
				new LikertItem
				{
					id = "alliance_trust",
					stem = "I felt comfortable following guidance from the virtual therapist or coach.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildTrustBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Strongly Disagree", "Disagree", "Neutral", "Agree", "Strongly Agree" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "trust_reliable",
					stem = "I found the system reliable.",
					mode = text
				},
				new LikertItem
				{
					id = "trust_predictable",
					stem = "The system behaved in a predictable way.",
					mode = text
				},
				new LikertItem
				{
					id = "trust_confident",
					stem = "I felt confident relying on the system.",
					mode = text
				},
				new LikertItem
				{
					id = "trust_safe",
					stem = "I felt safe using the system.",
					mode = text
				},
				new LikertItem
				{
					id = "trust_transparent",
					stem = "I understood what the system was doing.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildStressBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "stress_tense",
					stem = "I felt tense during the task.",
					mode = text
				},
				new LikertItem
				{
					id = "stress_pressure",
					stem = "I felt under pressure during the task.",
					mode = text
				},
				new LikertItem
				{
					id = "stress_overwhelmed",
					stem = "I felt overwhelmed during the task.",
					mode = text
				},
				new LikertItem
				{
					id = "stress_control",
					stem = "I felt in control during the task.",
					mode = text
				},
				new LikertItem
				{
					id = "stress_recovery",
					stem = "I was able to recover quickly from errors or interruptions.",
					mode = text
				}
			}
		};
	}

	private static LikertQuestionBank BuildCustomBank(StudyConfig config)
	{
		string text = ResolveBankMode(config);
		return new LikertQuestionBank
		{
			version = 1,
			scale = Math.Max(2, config.scale),
			default_mode = text,
			labels = new List<string> { "Very Low", "Low", "Neutral", "High", "Very High" },
			items = new List<LikertItem>
			{
				new LikertItem
				{
					id = "custom_construct_intensity",
					stem = "Please rate the intensity of the target construct in this experience.",
					mode = text
				},
				new LikertItem
				{
					id = "custom_construct_clarity",
					stem = "Please rate how clearly the experience reflected the target construct.",
					mode = text
				},
				new LikertItem
				{
					id = "custom_construct_confidence",
					stem = "Please rate how confident you are in your judgement.",
					mode = text
				}
			}
		};
	}

	private static string ResolveBankMode(StudyConfig config)
	{
		if (config.scale == 21)
		{
			return "slider";
		}
		if (!string.IsNullOrWhiteSpace(config.responseMode))
		{
			return config.responseMode.Trim().ToLowerInvariant();
		}
		return "slider";
	}

	private static void NormalizeConfig(StudyConfig config, string userRequest)
	{
		config.schemaVersion = "paxsm-study-config-v1";
		config.studyName = (string.IsNullOrWhiteSpace(config.studyName) ? "PAXSM Full Study" : config.studyName);
		config.studyVersion = "1.0";
		config.design = "questionnaire-only";
		string text = NormalizeConstructName(config.construct);
		config.construct = ((!string.IsNullOrWhiteSpace(text)) ? text : (string.IsNullOrWhiteSpace(config.construct) ? "cognitive_load" : config.construct.Trim().ToLowerInvariant()));
		config.participantNumber = Math.Max(1, config.participantNumber);
		config.participantId = $"P{config.participantNumber:000}";
		config.sessionNumber = Math.Max(1, config.sessionNumber);
		config.conditionIndex = Math.Max(1, config.conditionIndex);
		config.conditions = new List<string>();
		config.conditionLabel = "QuestionnaireOnly";
		config.counterbalancingOrder = "None";
		config.randomizeQuestions = false;
		config.questionBankResourcesPath = "QuestionBanks/Scale";
		config.responseMode = (string.IsNullOrWhiteSpace(config.responseMode) ? "slider" : config.responseMode.Trim().ToLowerInvariant());
		config.scale = Math.Max(2, config.scale);
		if (config.scale == 21)
		{
			config.responseMode = "slider";
		}
		config.outputFolder = "C:/PAXSM_FullStudy_Data";
		config.outputSubfolder = "ExportsCSV";
		config.fileNamePrefix = "PAXSM";
		config.exportMergedCsv = true;
		config.exportRawStageEvents = true;
		config.exportOnQuit = true;
		config.preventOverwrite = true;
		config.naturalLanguageRequest = userRequest;
		config.generatedSummary = (string.IsNullOrWhiteSpace(config.generatedSummary) ? BuildSummary(config.design, config.construct, config.responseMode, config.scale) : config.generatedSummary.Trim());
		ApplyInstrumentMetadata(config);
	}

	private static List<string> BuildWarnings(StudyConfig config)
	{
		List<string> list = new List<string>();
		if (config.randomizeQuestions)
		{
			list.Add("Question randomization is saved in config but needs Unity-side randomization support before relying on it.");
		}
		if (config.scale == 21)
		{
			list.Add("21-point scales are locked to slider mode in PAXSM.");
		}
		if (config.scale == 21 && ContainsAny((config.naturalLanguageRequest ?? "").ToLowerInvariant(), "card", "卡片"))
		{
			list.Add("21-point scales are locked to slider mode in PAXSM. The requested card mode was changed to slider.");
		}
		if (config.construct.Equals("usability", StringComparison.OrdinalIgnoreCase) && ContainsExactSusRequest(config.naturalLanguageRequest))
		{
			list.Add("SUS requires the standard 10 items, 5-point response scale, reverse scoring for alternating items, and 0-100 scoring.");
		}
		if (config.construct.Equals("cognitive_load", StringComparison.OrdinalIgnoreCase) && ContainsExactNasaTlxRequest(config.naturalLanguageRequest))
		{
			list.Add("If pairwise weighting is omitted, report this as Raw NASA-TLX / unweighted NASA-TLX. Objective task metrics such as accuracy and reaction time should be logged separately.");
		}
		if (config.construct.Equals("cognitive_load", StringComparison.OrdinalIgnoreCase) && ContainsSingleItemWorkloadRequest(config.naturalLanguageRequest))
		{
			list.Add("This is not full NASA-TLX. It measures mental demand as a single-item workload rating.");
		}
		if (config.construct.Equals("user_experience", StringComparison.OrdinalIgnoreCase) && ContainsExactUeqSRequest(config.naturalLanguageRequest))
		{
			list.Add("UEQ-S should be treated as standardized only after importing and verifying the original wording, bipolar response scale, and scoring.");
		}
		if (config.instrumentStatus.Equals("custom_generated", StringComparison.OrdinalIgnoreCase))
		{
			list.Add("This is a custom pilot questionnaire, not a validated scale. Do not report it as SUS, UEQ, IPQ, VRNQ, or another standardized questionnaire.");
			list.Add("Use standardized questionnaires if comparability with prior studies is required.");
		}
		if (config.instrumentStatus.Equals("standardized_recommended_not_deployed", StringComparison.OrdinalIgnoreCase))
		{
			list.Add("Standardized instrument recommended but not fully deployed unless the original wording, response scale, and scoring are imported and verified.");
		}
		return list;
	}

	private static List<string> MergeWarnings(List<string>? first, List<string> second)
	{
		List<string> list = new List<string>();
		if (first != null)
		{
			foreach (string item in first)
			{
				if (!string.IsNullOrWhiteSpace(item) && !list.Contains(item) && !IsNoisyModelWarning(item))
				{
					list.Add(item);
				}
			}
		}
		foreach (string item2 in second)
		{
			if (!string.IsNullOrWhiteSpace(item2) && !list.Contains(item2))
			{
				list.Add(item2);
			}
		}
		return list;
	}

	private static bool IsNoisyModelWarning(string warning)
	{
		return ContainsAny(warning, "garbled", "non-standard characters", "input text appears", "mojibake", "condition", "conditions", "conditionlabel", "counterbalancing", "within-group experimental design", "within-subjects design", "full study setup", "conditions array", "groups", "primary psychological construct", "did not specify the primary", "defaulted to measuring", "if you intended to measure");
	}

	private static List<string> BuildClarifyingQuestions(string lower, string construct, string design, int explicitScale, string detectedResponseMode, bool allowDefaults)
	{
		List<string> list = new List<string>();
		AddQuestionIfNeeded(list, !HasExplicitConstruct(lower) || construct == "custom", "你主要想测哪个构念？例如：认知负荷、系统可用性、presence、trust、stress，或者写出你的自定义构念。");
		AddQuestionIfNeeded(list, !HasExplicitDesign(lower), "实验设计是 within-subjects / within group，还是 between-subjects / between group？");
		AddQuestionIfNeeded(list, !allowDefaults && explicitScale <= 0, BuildScaleQuestion(construct));
		AddQuestionIfNeeded(list, explicitScale != 21 && string.IsNullOrWhiteSpace(detectedResponseMode), "回答方式必须明确选择：slider 还是 card？如果选择 21 点量表，系统会自动强制使用 slider。");
		AddQuestionIfNeeded(list, HasExplicitWithinDesign(lower) && !HasConditionInfo(lower), "within-subjects 需要哪些实验条件？例如：A/B、slider/card、low/high feedback，或你自己的 condition 名称。");
		return list;
	}

	private static void AddQuestionIfNeeded(List<string> questions, bool condition, string question)
	{
		if (condition && questions.Count < 3)
		{
			questions.Add(question);
		}
	}

	private static string BuildScaleQuestion(string construct)
	{
		if (!(construct == "cognitive_load"))
		{
			if (construct == "usability")
			{
				return "你希望使用几点 Likert 量表？系统可用性如果接近 SUS，标准是 5 点；你也可以选择 7 点或 21 点作为改编版。";
			}
			return "你希望使用几点 Likert/评分量表？例如 5 点、7 点或 21 点。";
		}
		return "你希望使用几点 Likert/评分量表？认知负荷建议 21 点（与你当前 PAXSM 设置一致）或 7 点；请明确写一个数字。";
	}

	private static bool AllowsAgentDefaults(string lower)
	{
		return ContainsAny(lower, "use default", "use defaults", "default setting", "defaults", "you decide", "decide for me", "recommend", "auto", "默认", "你决定", "你来定", "帮我决定", "自动", "推荐设置", "按推荐");
	}

	private static bool HasExplicitConstruct(string lower)
	{
		return ContainsAny(lower, "cognitive load", "workload", "nasa", "tlx", "usability", "sus", "user experience", "ux", "presence", "spatial presence", "social presence", "physical demand", "physical burden", "perceived exertion", "borg", "rpe", "safety", "comfort", "continuance", "intention to use", "adoption", "acceptance", "adherence", "self-presence", "immersion", "embodiment", "motion sickness", "simulator sickness", "cybersickness", "simulator realism", "virtual therapist", "therapist alliance", "trust", "stress", "认知负荷", "工作负荷", "可用性", "易用性", "用户体验", "系统可用性", "沉浸", "临场感", "社交临场", "自我临场", "身体所有感", "化身", "晕动", "恶心", "眩晕", "模拟真实感", "保真度", "虚拟治疗师", "治疗联盟", "信任", "压力");
	}

	private static bool HasExplicitDesign(string lower)
	{
		return ContainsAny(lower, "within", "within-subject", "within subject", "within group", "between", "between-subject", "between subject", "between group", "组内", "被试内", "重复测量", "组间", "被试间");
	}

	private static bool HasExplicitWithinDesign(string lower)
	{
		return ContainsAny(lower, "within", "within-subject", "within subject", "within group", "组内", "被试内", "重复测量");
	}

	private static bool HasExplicitResponseMode(string lower)
	{
		return ContainsAny(lower, "slider", "card", "cards", "knob", "滑杆", "滑条", "卡片", "旋钮");
	}

	private static bool HasConditionInfo(string lower)
	{
		if (ContainsAny(lower, "condition", "conditions", "条件", "组别", "a/b", "a vs b", "level", "levels"))
		{
			return true;
		}
		return Regex.IsMatch(lower, "\\b[a-z0-9]+\\s*(/|vs|versus)\\s*[a-z0-9]+\\b", RegexOptions.IgnoreCase);
	}

	private static string ExtractJsonObject(string content)
	{
		if (TryExtractJsonObject(content, out string json))
		{
			return json;
		}
		throw new InvalidOperationException("No JSON object found in LLM output.");
	}

	private static bool TryExtractJsonObject(string content, out string json)
	{
		int num = content.IndexOf('{');
		int num2 = content.LastIndexOf('}');
		if (num < 0 || num2 <= num)
		{
			json = "";
			return false;
		}
		json = content.Substring(num, num2 - num + 1);
		return true;
	}

	private static string DetectDesign(string lower)
	{
		int num = LastIndexOfAny(lower, "within", "within-subject", "within subject", "within group", "组内", "被试内", "重复测量");
		int num2 = LastIndexOfAny(lower, "between", "between-subject", "between subject", "between group", "组间", "被试间");
		if (num >= 0 || num2 >= 0)
		{
			if (num < num2)
			{
				return "between-subjects";
			}
			return "within-subjects";
		}
		return "within-subjects";
	}

	private static string DetectConstruct(string lower)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		dictionary["cognitive_load"] = LastIndexOfAny(lower, "认知负荷", "工作负荷", "cognitive load", "workload", "nasa", "tlx");
		dictionary["usability"] = LastIndexOfAny(lower, "usability", "可用性", "易用性", "用户体验", "系统可用性", "sus");
		dictionary["user_experience"] = LastIndexOfAny(lower, "user experience", "ux", "vrsuq", "ux in ive", "整体体验");
		dictionary["physical_demand"] = LastIndexOfAny(lower, "physical demand", "physical burden", "perceived exertion", "borg", "rpe", "fatigue", "upper limb", "rehab training", "身体负担", "身体负荷", "体力负担", "上肢运动", "康复训练");
		dictionary["safety_comfort"] = LastIndexOfAny(lower, "safety", "safe", "comfort", "pain", "injury risk", "stability", "安全感", "安全", "舒适", "疼痛", "风险", "稳定");
		dictionary["continuance_intention"] = LastIndexOfAny(lower, "willingness to continue", "continue using", "intention to use", "adoption", "acceptance", "adherence", "tam", "utaut", "愿意继续使用", "继续使用", "使用意愿", "接受度");
		dictionary["motion_sickness"] = LastIndexOfAny(lower, "motion sickness", "simulator sickness", "cybersickness", "vr sickness", "ssq", "vrsq", "晕动", "恶心", "眩晕");
		dictionary["presence_social"] = LastIndexOfAny(lower, "social presence", "co-presence", "copresence", "社交临场", "共在");
		dictionary["presence_self"] = LastIndexOfAny(lower, "self-presence", "embodiment", "body ownership", "avatar", "自我临场", "身体所有感", "化身");
		dictionary["presence"] = LastIndexOfAny(lower, "presence", "immersion", "沉浸", "临场感");
		dictionary["simulator_realism"] = LastIndexOfAny(lower, "simulator realism", "simulation realism", "training realism", "visual realism", "模拟真实感", "保真度");
		dictionary["virtual_therapist_alliance"] = LastIndexOfAny(lower, "virtual therapist", "therapist alliance", "virtual coach", "虚拟治疗师", "治疗联盟");
		dictionary["trust"] = LastIndexOfAny(lower, "trust", "信任");
		dictionary["stress"] = LastIndexOfAny(lower, "stress", "压力");
		string result = "custom";
		int num = -1;
		foreach (KeyValuePair<string, int> item in dictionary)
		{
			if (item.Value > num)
			{
				result = item.Key;
				num = item.Value;
			}
		}
		if (num < 0)
		{
			return "custom";
		}
		return result;
	}

	private static int DetectScale(string lower)
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		dictionary[21] = LastIndexOfAny(lower, "21", "二十一", "21-point");
		dictionary[7] = LastIndexOfAny(lower, "7", "七点", "7-point");
		dictionary[5] = LastIndexOfAny(lower, "5", "五点", "5-point");
		int result = 0;
		int num = -1;
		foreach (KeyValuePair<int, int> item in dictionary)
		{
			if (item.Value > num)
			{
				result = item.Key;
				num = item.Value;
			}
		}
		if (num < 0)
		{
			return 0;
		}
		return result;
	}

	private static int ResolveRequestedScale(string userRequest, int defaultScale)
	{
		string text = (userRequest ?? "").ToLowerInvariant();
		Match match = Regex.Match(text, "(?<!\\d)(21|7|5|4)(?!\\d)\\s*(?:\\u70b9|point|points|pt|pts)?", RegexOptions.IgnoreCase);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result))
		{
			return result;
		}
		int num = DetectScale(text);
		if (num <= 0)
		{
			return defaultScale;
		}
		return num;
	}

	private static int ResolveRequestedScaleForConstruct(string userRequest, string construct, int defaultScale)
	{
		string text = (userRequest ?? "").ToLowerInvariant();
		if (construct.Equals("usability", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "sus", "system usability scale"))
		{
			return ExtractScaleAttachedToInstrument(text, "sus", 5);
		}
		if (construct.Equals("cognitive_load", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "nasa-tlx", "nasa tlx", "nasa_tlx", "tlx", "cognitive load", "认知负荷"))
		{
			int num = ExtractScaleAttachedToInstrument(text, "nasa-tlx", 0);
			if (num <= 0)
			{
				num = ExtractScaleAttachedToInstrument(text, "nasa tlx", 0);
			}
			if (num <= 0)
			{
				num = ExtractScaleAttachedToInstrument(text, "tlx", 0);
			}
			if (num <= 0)
			{
				return 21;
			}
			return num;
		}
		if (construct.Equals("motion_sickness", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "ssq", "simulator sickness questionnaire", "fms", "vrsq", "晕动", "晕vr"))
		{
			return ExtractScaleAttachedToInstrument(text, "ssq", 4);
		}
		if (construct.Equals("user_experience", StringComparison.OrdinalIgnoreCase) && ContainsExactUeqSRequest(text))
		{
			return ExtractScaleNearInstrument(text, "ueq", defaultScale);
		}
		return ResolveRequestedScale(userRequest ?? "", defaultScale);
	}

	private static int ExtractScaleAttachedToInstrument(string lower, string instrumentNeedle, int fallback)
	{
		if (string.IsNullOrWhiteSpace(lower) || string.IsNullOrWhiteSpace(instrumentNeedle))
		{
			return fallback;
		}
		string pattern = Regex.Escape(instrumentNeedle) + "[^。,.，;；\\r\\n]{0,32}(?<!\\d)(21|7|5|4)(?!\\d)\\s*(?:\\u70b9|point|points|pt|pts)?";
		Match match = Regex.Match(lower, pattern, RegexOptions.IgnoreCase);
		if (!match.Success || !int.TryParse(match.Groups[1].Value, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static int ExtractScaleNearInstrument(string lower, string instrumentNeedle, int fallback)
	{
		if (string.IsNullOrWhiteSpace(lower))
		{
			return fallback;
		}
		int num = lower.IndexOf(instrumentNeedle, StringComparison.OrdinalIgnoreCase);
		if (num < 0 && instrumentNeedle.Equals("tlx", StringComparison.OrdinalIgnoreCase))
		{
			num = lower.IndexOf("cognitive load", StringComparison.OrdinalIgnoreCase);
		}
		if (num < 0)
		{
			return fallback;
		}
		Match match = Regex.Match(lower.Substring(num, Math.Min(lower.Length - num, 80)), "(?<!\\d)(21|7|5|4)(?!\\d)\\s*(?:\\u70b9|point|points|pt|pts)?", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			int num2 = Math.Max(0, num - 28);
			int length = Math.Min(lower.Length - num2, 80);
			match = Regex.Match(lower.Substring(num2, length), "(?<!\\d)(21|7|5|4)(?!\\d)\\s*(?:\\u70b9|point|points|pt|pts)?", RegexOptions.IgnoreCase);
		}
		if (!match.Success || !int.TryParse(match.Groups[1].Value, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static string ResolveRequestedResponseMode(string userRequest, int scale, string defaultResponseMode)
	{
		if (scale == 21)
		{
			return "slider";
		}
		string text = DetectResponseMode((userRequest ?? "").ToLowerInvariant());
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return defaultResponseMode;
	}

	private static string ResolveRequestedResponseModeForConstruct(string userRequest, string construct, int scale, string defaultResponseMode)
	{
		if (scale == 21)
		{
			return "slider";
		}
		string text = (userRequest ?? "").ToLowerInvariant();
		if (construct.Equals("usability", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "sus", "system usability scale"))
		{
			return "card";
		}
		if (construct.Equals("motion_sickness", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "ssq", "simulator sickness questionnaire", "fms", "vrsq"))
		{
			return "card";
		}
		string text2 = DetectResponseModeNearConstruct(text, construct);
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		if (!string.IsNullOrWhiteSpace(defaultResponseMode))
		{
			return defaultResponseMode;
		}
		return "card";
	}

	private static string DetectResponseModeNearConstruct(string lower, string construct)
	{
		string[] array = construct switch
		{
			"cognitive_load" => new string[5] { "nasa-tlx", "nasa tlx", "tlx", "cognitive load", "认知负荷" }, 
			"usability" => new string[3] { "sus", "usability", "可用性" }, 
			"motion_sickness" => new string[4] { "ssq", "fms", "vrsq", "晕动" }, 
			"user_experience" => new string[4] { "ueq", "ux", "user experience", "用户体验" }, 
			_ => Array.Empty<string>(), 
		};
		foreach (string value in array)
		{
			int num = lower.IndexOf(value, StringComparison.OrdinalIgnoreCase);
			if (num >= 0)
			{
				int num2 = Math.Max(0, num - 28);
				int length = Math.Min(lower.Length - num2, 90);
				string text = DetectResponseMode(lower.Substring(num2, length));
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
		}
		return "";
	}

	private static int DefaultScaleForConstruct(string construct)
	{
		return construct switch
		{
			"cognitive_load" => 21, 
			"usability" => 5, 
			"motion_sickness" => 4, 
			_ => 7, 
		};
	}

	private static string ResolveResponseMode(int explicitScale, string detectedResponseMode)
	{
		if (explicitScale == 21)
		{
			return "slider";
		}
		if (!string.IsNullOrWhiteSpace(detectedResponseMode))
		{
			return detectedResponseMode;
		}
		return "";
	}

	private static string DetectResponseMode(string lower)
	{
		int num = LastIndexOfAny(lower, "card", "cards", "卡片");
		int num2 = LastIndexOfAny(lower, "slider", "滑杆", "滑条");
		int num3 = LastIndexOfAny(lower, "knob", "旋钮");
		if (num >= 0 || num2 >= 0 || num3 >= 0)
		{
			if (num >= num2 && num >= num3)
			{
				return "card";
			}
			return "slider";
		}
		return "";
	}

	private static int DetectNumber(string lower, string pattern, int fallback)
	{
		Match match = Regex.Match(lower, pattern, RegexOptions.IgnoreCase);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result) && result > 0)
		{
			return result;
		}
		return fallback;
	}

	private static List<string> DetectConditions(string lower)
	{
		List<string> list = new List<string>();
		Match match = Regex.Match(lower, "\\b([a-z][a-z0-9_-]*)\\s*/\\s*([a-z][a-z0-9_-]*)\\b", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			AddCondition(list, match.Groups[1].Value);
			AddCondition(list, match.Groups[2].Value);
			return list;
		}
		Match match2 = Regex.Match(lower, "\\b([a-z][a-z0-9_-]*)\\s*(?:vs|versus)\\s*([a-z][a-z0-9_-]*)\\b", RegexOptions.IgnoreCase);
		if (match2.Success)
		{
			AddCondition(list, match2.Groups[1].Value);
			AddCondition(list, match2.Groups[2].Value);
		}
		return list;
	}

	private static void AddCondition(List<string> conditions, string value)
	{
		string text = Regex.Replace(value.Trim(), "[^a-zA-Z0-9_-]", "");
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		foreach (string condition in conditions)
		{
			if (string.Equals(condition, text, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		conditions.Add(text.ToUpperInvariant());
	}

	private static string BuildConditionLabel(string constructLabel, string design, List<string> conditions)
	{
		string text = constructLabel + "_" + DesignShortName(design);
		if (conditions.Count > 0)
		{
			text = text + "_" + string.Join("-", conditions);
		}
		return text;
	}

	private static int LastIndexOfAny(string text, params string[] needles)
	{
		int num = -1;
		foreach (string value in needles)
		{
			if (!string.IsNullOrEmpty(value))
			{
				int num2 = text.LastIndexOf(value, StringComparison.OrdinalIgnoreCase);
				if (num2 > num)
				{
					num = num2;
				}
			}
		}
		return num;
	}

	private static bool ContainsAny(string text, params string[] needles)
	{
		foreach (string value in needles)
		{
			if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static string DesignShortName(string design)
	{
		if (design.Contains("within", StringComparison.OrdinalIgnoreCase))
		{
			return "Within";
		}
		if (design.Contains("between", StringComparison.OrdinalIgnoreCase))
		{
			return "Between";
		}
		return "Design";
	}

	private static string BuildSummary(string design, string construct, string responseMode, int scale)
	{
		return $"Configured a {design} PAXSM study for {construct}, using {responseMode} responses on a {scale}-point scale with merged CSV and raw stage-event logging.";
	}
}
