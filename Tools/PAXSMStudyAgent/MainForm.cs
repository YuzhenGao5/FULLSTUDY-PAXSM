using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PAXSMStudyAgent;

internal sealed class MainForm : Form
{
	private sealed class QuestionnaireTaskItem
	{
		public StudyConfig Config { get; init; } = new StudyConfig();

		public string Summary { get; init; } = "";

		public string Display { get; init; } = "";

		public override string ToString()
		{
			return Display;
		}
	}

	private static readonly Color AppBackground = Color.FromArgb(243, 242, 241);

	private static readonly Color Surface = Color.White;

	private static readonly Color SurfaceAlt = Color.FromArgb(250, 249, 248);

	private static readonly Color InputBackground = Color.FromArgb(255, 255, 255);

	private static readonly Color Border = Color.FromArgb(225, 223, 221);

	private static readonly Color TextPrimary = Color.FromArgb(32, 31, 30);

	private static readonly Color TextSecondary = Color.FromArgb(96, 94, 92);

	private static readonly Color TextMuted = Color.FromArgb(121, 119, 117);

	private static readonly Color Accent = Color.FromArgb(0, 120, 212);

	private static readonly Color AccentHover = Color.FromArgb(16, 110, 190);

	private static readonly Color AccentPressed = Color.FromArgb(0, 90, 158);

	private static readonly Color AgentBubble = Color.FromArgb(243, 242, 241);

	private static readonly Color ResearcherBubble = Color.FromArgb(222, 235, 255);

	private static readonly Color WarningBubble = Color.FromArgb(255, 244, 206);

	private static readonly Color Success = Color.FromArgb(16, 124, 16);

	private static readonly Color Error = Color.FromArgb(209, 52, 56);

	private readonly AgentEngine _agentEngine = new AgentEngine();

	private readonly ProjectConfigurator _configurator;

	private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

	private RichTextBox _chatBox;

	private TextBox _promptBox;

	private TextBox _previewBox;

	private TextBox _jsonPreviewBox;

	private TextBox _apiKeyBox;

	private TextBox _modelBox;

	private TextBox _projectRootBox;

	private TextBox _exportFolderBox;

	private TextBox _exportSubfolderBox;

	private TextBox _fileNamePrefixBox;

	private TextBox _conditionLabelBox;

	private NumericUpDown _participantNumberBox;

	private NumericUpDown _sessionNumberBox;

	private NumericUpDown _conditionIndexBox;

	private ComboBox _providerBox;

	private ListBox _questionnaireList;

	private Label _questionnaireListStatusLabel;

	private ListBox _sceneList;

	private TextBox _sceneDetailsBox;

	private Label _sceneStatusLabel;

	private Button _removeQuestionnaireButton;

	private Button _clearQuestionnairesButton;

	private Button _generateButton;

	private Button _applyButton;

	private Button _validateButton;

	private Label _connectionLabel;

	private Label _llmStatusLabel;

	private Label _statusLabel;

	private readonly System.Windows.Forms.Timer _thinkingTimer = new System.Windows.Forms.Timer();

	private readonly string[] _thinkingFrames = new string[4] { "◐", "◓", "◑", "◒" };

	private StudyConfig? _currentConfig;

	private string _pendingRequest = "";

	private bool _previewReadyForApply;

	private string _lastPreviewRequest = "";

	private bool _loadingExportSettings;

	private int _thinkingFrameIndex;

	private int _thinkingMessageStart = -1;

	private int _thinkingMessageLength;

	private int _thinkingBubbleStart = -1;

	private int _thinkingBubbleLength;

	public MainForm()
	{
		_configurator = new ProjectConfigurator(_agentEngine);
		InitializeComponent();
		_thinkingTimer.Interval = 150;
		_thinkingTimer.Tick += delegate
		{
			AdvanceThinkingIndicator();
		};
		LocateProjectRoot();
		SeedConversation();
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		_thinkingTimer.Stop();
		_thinkingTimer.Dispose();
		_disposeCts.Cancel();
		_disposeCts.Dispose();
		base.OnFormClosed(e);
	}

	private static Label DarkLabel(string text, int topPadding = 0)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			ForeColor = TextSecondary,
			Font = new Font("Segoe UI Semibold", 9f),
			Padding = new Padding(0, topPadding, 0, 4)
		};
	}

	private static void StylePrimaryButton(Button button)
	{
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = Accent;
		button.ForeColor = Color.White;
		button.FlatAppearance.BorderColor = Accent;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = AccentHover;
		button.FlatAppearance.MouseDownBackColor = AccentPressed;
		button.Padding = new Padding(14, 5, 14, 5);
		button.Font = new Font("Segoe UI Semibold", 9.5f);
		button.Cursor = Cursors.Hand;
	}

	private static void StyleSecondaryButton(Button button)
	{
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = Surface;
		button.ForeColor = TextPrimary;
		button.FlatAppearance.BorderColor = Border;
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(237, 235, 233);
		button.Padding = new Padding(14, 5, 14, 5);
		button.Font = new Font("Segoe UI", 9.5f);
		button.Cursor = Cursors.Hand;
	}

	private void InitializeComponent()
	{
		this.Text = "PAXSM Study Setup Agent";
		this.MinimumSize = new System.Drawing.Size(1100, 720);
		base.Size = new System.Drawing.Size(1360, 860);
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Font = new System.Drawing.Font("Segoe UI", 10f);
		this.BackColor = PAXSMStudyAgent.MainForm.AppBackground;
		System.Windows.Forms.TableLayoutPanel tableLayoutPanel = new System.Windows.Forms.TableLayoutPanel
		{
			Dock = System.Windows.Forms.DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 2,
			Padding = new System.Windows.Forms.Padding(18),
			BackColor = PAXSMStudyAgent.MainForm.AppBackground
		};
		tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 52f));
		tableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 48f));
		base.Controls.Add(tableLayoutPanel);
		System.Windows.Forms.Control control = this.BuildHeaderPanel();
		tableLayoutPanel.Controls.Add(control, 0, 0);
		tableLayoutPanel.SetColumnSpan(control, 2);
		tableLayoutPanel.Controls.Add(this.BuildChatPanel(), 0, 1);
		tableLayoutPanel.Controls.Add(this.BuildConfigPanel(), 1, 1);
	}

	private Control BuildHeaderPanel()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(0, 0, 0, 14),
			BackColor = AppBackground,
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Percent, 100f),
				new ColumnStyle(SizeType.AutoSize)
			}
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = AppBackground
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(new Label
		{
			AutoSize = true,
			Text = "PAXSM Study Setup Agent",
			Font = new Font("Segoe UI Semibold", 16f),
			ForeColor = TextPrimary
		}, 0, 0);
		tableLayoutPanel.Controls.Add(new Label
		{
			AutoSize = true,
			Text = "Questionnaire setup workspace",
			Font = new Font("Segoe UI", 9.5f),
			ForeColor = TextSecondary,
			Padding = new Padding(0, 2, 0, 0)
		}, 0, 1);
		obj.Controls.Add(tableLayoutPanel, 0, 0);
		Label control = new Label
		{
			AutoSize = true,
			Text = "Preview mode",
			Font = new Font("Segoe UI Semibold", 9f),
			ForeColor = Accent,
			BackColor = Color.FromArgb(235, 245, 255),
			Padding = new Padding(12, 6, 12, 6),
			Margin = new Padding(0, 6, 0, 0)
		};
		obj.Controls.Add(control, 1, 0);
		return obj;
	}

	private Control BuildChatPanel()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 5,
			ColumnCount = 1,
			Padding = new Padding(0, 0, 10, 0),
			BackColor = AppBackground,
			RowStyles = 
			{
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.Percent, 100f),
				new RowStyle(SizeType.Absolute, 120f),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize)
			}
		};
		Label control = new Label
		{
			Text = "Conversation",
			AutoSize = true,
			Font = new Font("Segoe UI Semibold", 11.5f),
			ForeColor = TextPrimary,
			Padding = new Padding(0, 0, 0, 8)
		};
		obj.Controls.Add(control, 0, 0);
		_chatBox = new RichTextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			ScrollBars = RichTextBoxScrollBars.Vertical,
			BackColor = Surface,
			ForeColor = TextPrimary,
			BorderStyle = BorderStyle.FixedSingle,
			DetectUrls = false,
			Font = new Font("Segoe UI", 10.5f)
		};
		obj.Controls.Add(_chatBox, 0, 1);
		_promptBox = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ScrollBars = ScrollBars.Vertical,
			AcceptsReturn = true,
			Text = "我要做一个within group实验，这个实验我要测认知负荷，使用21点slider，保存完整反应时间和raw stage events。",
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = InputBackground,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 10.5f)
		};
		_promptBox.TextChanged += delegate
		{
			InvalidatePreview();
		};
		obj.Controls.Add(_promptBox, 0, 2);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			AutoSize = true,
			Padding = new Padding(0, 8, 0, 8),
			BackColor = AppBackground
		};
		_generateButton = new Button
		{
			Text = "Generate preview",
			AutoSize = true,
			Height = 36
		};
		StylePrimaryButton(_generateButton);
		_generateButton.Click += async delegate
		{
			await GenerateConfigAsync();
		};
		flowLayoutPanel.Controls.Add(_generateButton);
		_applyButton = new Button
		{
			Text = "Confirm and apply",
			AutoSize = true,
			Height = 36
		};
		StyleSecondaryButton(_applyButton);
		_applyButton.Click += async delegate
		{
			await ApplyConfigAsync();
		};
		flowLayoutPanel.Controls.Add(_applyButton);
		_validateButton = new Button
		{
			Text = "Validate project",
			AutoSize = true,
			Height = 36
		};
		StyleSecondaryButton(_validateButton);
		_validateButton.Click += delegate
		{
			ValidateProject();
		};
		flowLayoutPanel.Controls.Add(_validateButton);
		obj.Controls.Add(flowLayoutPanel, 0, 3);
		_statusLabel = new Label
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			ForeColor = TextSecondary,
			Text = "Ready"
		};
		obj.Controls.Add(_statusLabel, 0, 4);
		return obj;
	}

	private Control BuildConfigPanel()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 12,
			ColumnCount = 1,
			Padding = new Padding(16),
			BackColor = Surface,
			RowStyles = 
			{
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.Absolute, 166f),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.Absolute, 176f),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.Percent, 100f),
				new RowStyle(SizeType.AutoSize)
			}
		};
		Label control = new Label
		{
			Text = "Configuration workspace",
			AutoSize = true,
			Font = new Font("Segoe UI Semibold", 11.5f),
			ForeColor = TextPrimary,
			Padding = new Padding(0, 0, 0, 8)
		};
		obj.Controls.Add(control, 0, 0);
		obj.Controls.Add(DarkLabel("Unity project root"), 0, 1);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = Surface
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		_projectRootBox = new TextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = SurfaceAlt,
			ForeColor = TextPrimary
		};
		tableLayoutPanel.Controls.Add(_projectRootBox, 0, 0);
		Button button = new Button
		{
			Text = "Browse",
			AutoSize = true
		};
		StyleSecondaryButton(button);
		button.Click += delegate
		{
			BrowseProjectRoot();
		};
		tableLayoutPanel.Controls.Add(button, 1, 0);
		Button scenesButton = new Button
		{
			Text = "Scenes...",
			AutoSize = true
		};
		StyleSecondaryButton(scenesButton);
		scenesButton.Click += delegate
		{
			ShowScenesWindow();
		};
		tableLayoutPanel.Controls.Add(scenesButton, 2, 0);
		obj.Controls.Add(tableLayoutPanel, 0, 2);
		obj.Controls.Add(DarkLabel("Experiment questionnaire tasks", 8), 0, 3);
		obj.Controls.Add(BuildQuestionnaireListPanel(), 0, 4);
		obj.Controls.Add(DarkLabel("Data export settings", 8), 0, 5);
		obj.Controls.Add(BuildExportSettingsPanel(), 0, 6);
		_connectionLabel = DarkLabel("LLM connection", 8);
		obj.Controls.Add(_connectionLabel, 0, 7);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = Surface
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
		_providerBox = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = InputBackground,
			ForeColor = TextPrimary,
			FlatStyle = FlatStyle.Flat
		};
		_providerBox.Items.Add("Ollama");
		_providerBox.Items.Add("OpenAI-compatible");
		string text = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
		bool flag = !string.IsNullOrWhiteSpace(text);
		_providerBox.SelectedItem = (flag ? "OpenAI-compatible" : "Ollama");
		_providerBox.SelectedIndexChanged += delegate
		{
			UpdateProviderUi();
		};
		tableLayoutPanel2.Controls.Add(_providerBox, 0, 0);
		_apiKeyBox = new TextBox
		{
			Dock = DockStyle.Fill,
			PlaceholderText = "http://localhost:11434",
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = InputBackground,
			ForeColor = TextPrimary
		};
		_apiKeyBox.Text = (flag ? text : (Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434"));
		_apiKeyBox.TextChanged += delegate
		{
			UpdateLlmStatus();
		};
		tableLayoutPanel2.Controls.Add(_apiKeyBox, 1, 0);
		_modelBox = new TextBox
		{
			Dock = DockStyle.Fill,
			PlaceholderText = "Model",
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = InputBackground,
			ForeColor = TextPrimary,
			Text = (flag ? (Environment.GetEnvironmentVariable("PAXSM_OPENAI_MODEL") ?? "gpt-4.1-mini") : (Environment.GetEnvironmentVariable("PAXSM_OLLAMA_MODEL") ?? "qwen3:14b"))
		};
		_modelBox.TextChanged += delegate
		{
			UpdateLlmStatus();
		};
		tableLayoutPanel2.Controls.Add(_modelBox, 2, 0);
		obj.Controls.Add(tableLayoutPanel2, 0, 8);
		_llmStatusLabel = new Label
		{
			AutoSize = true,
			ForeColor = TextSecondary,
			Text = "LLM: Local Ollama selected.",
			Padding = new Padding(0, 4, 0, 8)
		};
		obj.Controls.Add(_llmStatusLabel, 0, 9);
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Fill,
			Appearance = TabAppearance.Normal
		};
		_previewBox = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			WordWrap = true,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Surface,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 10f)
		};
		_jsonPreviewBox = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Both,
			WordWrap = false,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Color.FromArgb(246, 248, 250),
			ForeColor = TextPrimary,
			Font = new Font("Consolas", 9.5f)
		};
		TabPage value = new TabPage("Overview")
		{
			BackColor = Surface,
			Controls = { (Control?)_previewBox }
		};
		tabControl.TabPages.Add(value);
		TabPage scenesPage = new TabPage("Unity Scenes")
		{
			BackColor = Surface,
			Controls = { BuildScenesPanel() }
		};
		tabControl.TabPages.Add(scenesPage);
		TabPage value2 = new TabPage("Technical JSON")
		{
			BackColor = Color.FromArgb(246, 248, 250),
			Controls = { (Control?)_jsonPreviewBox }
		};
		tabControl.TabPages.Add(value2);
		obj.Controls.Add(tabControl, 0, 10);
		Label control2 = new Label
		{
			AutoSize = true,
			ForeColor = TextMuted,
			Text = "Overview and technical audit",
			Padding = new Padding(0, 8, 0, 0)
		};
		obj.Controls.Add(control2, 0, 11);
		UpdateProviderUi();
		return obj;
	}

	private Control BuildScenesPanel()
	{
		TableLayoutPanel panel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Padding = new Padding(10),
			BackColor = Surface
		};
		panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		FlowLayoutPanel toolbar = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			BackColor = Surface,
			Padding = new Padding(0, 0, 0, 6)
		};
		Button refreshButton = new Button
		{
			Text = "Refresh scenes",
			AutoSize = true,
			Height = 30
		};
		StyleSecondaryButton(refreshButton);
		refreshButton.Click += delegate
		{
			RefreshScenes(announce: true);
		};
		toolbar.Controls.Add(refreshButton);
		Label hint = new Label
		{
			AutoSize = true,
			Text = "Read-only discovery from Assets/Scenes and Build Settings",
			ForeColor = TextMuted,
			Padding = new Padding(8, 7, 0, 0)
		};
		toolbar.Controls.Add(hint);
		panel.Controls.Add(toolbar, 0, 0);

		SplitContainer split = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Vertical,
			BackColor = Border
		};
		split.SizeChanged += delegate
		{
			if (split.Width > 480)
			{
				int desired = Math.Clamp((int)(split.Width * 0.42f), 180, split.Width - 220);
				if (split.SplitterDistance != desired)
				{
					split.SplitterDistance = desired;
				}
			}
		};
		_sceneList = new ListBox
		{
			Dock = DockStyle.Fill,
			IntegralHeight = false,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = SurfaceAlt,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 9.5f)
		};
		_sceneList.SelectedIndexChanged += delegate
		{
			ShowSelectedSceneDetails();
		};
		split.Panel1.Controls.Add(_sceneList);
		_sceneDetailsBox = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			WordWrap = true,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Surface,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 9.5f)
		};
		split.Panel2.Controls.Add(_sceneDetailsBox);
		panel.Controls.Add(split, 0, 1);

		_sceneStatusLabel = new Label
		{
			AutoSize = true,
			ForeColor = TextMuted,
			Padding = new Padding(0, 6, 0, 0),
			Text = "Scenes have not been scanned yet."
		};
		panel.Controls.Add(_sceneStatusLabel, 0, 2);
		return panel;
	}

	private void RefreshScenes(bool announce)
	{
		if (_sceneList == null || _sceneDetailsBox == null || _sceneStatusLabel == null)
		{
			return;
		}

		string previouslySelected = (_sceneList.SelectedItem as UnitySceneInfo)?.RelativePath ?? "";
		_sceneList.BeginUpdate();
		try
		{
			_sceneList.Items.Clear();
			IReadOnlyList<UnitySceneInfo> scenes = _configurator.DiscoverScenes();
			foreach (UnitySceneInfo scene in scenes)
			{
				_sceneList.Items.Add(scene);
			}

			_sceneStatusLabel.Text = $"Found {scenes.Count} scene(s). Selection is read-only.";
			if (scenes.Count == 0)
			{
				_sceneDetailsBox.Text = "No .unity scenes were found under Assets/Scenes.";
			}
			else
			{
				int selectedIndex = scenes
					.Select((scene, index) => new { scene, index })
					.Where(item => string.Equals(item.scene.RelativePath, previouslySelected, StringComparison.OrdinalIgnoreCase))
					.Select(item => item.index)
					.DefaultIfEmpty(-1)
					.First();
				if (selectedIndex < 0)
				{
					selectedIndex = scenes
						.Select((scene, index) => new { scene, index })
						.Where(item => item.scene.IsBuildEnabled)
						.Select(item => item.index)
						.DefaultIfEmpty(0)
						.First();
				}
				_sceneList.SelectedIndex = selectedIndex;
			}

			if (announce)
			{
				AppendChat("Agent", $"Read {scenes.Count} Unity scene(s) from Assets/Scenes. No scene files were changed.");
			}
		}
		catch (Exception ex)
		{
			_sceneStatusLabel.Text = "Scene scan failed.";
			_sceneDetailsBox.Text = ex.Message;
			if (announce)
			{
				AppendChat("Agent warning", "Could not read Unity scenes: " + ex.Message);
			}
		}
		finally
		{
			_sceneList.EndUpdate();
		}
	}

	private void ShowSelectedSceneDetails()
	{
		if (_sceneDetailsBox == null)
		{
			return;
		}
		_sceneDetailsBox.Text = (_sceneList?.SelectedItem as UnitySceneInfo)?.Details() ??
			"Select a Unity scene to inspect it.";
	}

	private void ShowScenesWindow()
	{
		Form dialog = new Form
		{
			Text = "Unity Scenes",
			StartPosition = FormStartPosition.CenterParent,
			Size = new Size(1040, 720),
			MinimumSize = new Size(820, 580),
			BackColor = AppBackground,
			Font = new Font("Segoe UI", 9.5f),
			ShowInTaskbar = false
		};
		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			Padding = new Padding(16),
			BackColor = Surface
		};
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		Label title = new Label
		{
			AutoSize = true,
			Text = "Scenes discovered from Assets/Scenes",
			Font = new Font("Segoe UI Semibold", 12f),
			ForeColor = TextPrimary,
			Padding = new Padding(0, 0, 0, 10)
		};
		layout.Controls.Add(title, 0, 0);

		SplitContainer split = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Vertical,
			BackColor = Border
		};
		split.SizeChanged += delegate
		{
			if (split.Width > 600)
			{
				int desired = Math.Clamp((int)(split.Width * 0.42f), 260, split.Width - 300);
				if (split.SplitterDistance != desired)
				{
					split.SplitterDistance = desired;
				}
			}
		};
		ListBox list = new ListBox
		{
			Dock = DockStyle.Fill,
			IntegralHeight = false,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = SurfaceAlt,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 10f)
		};
		TextBox details = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			WordWrap = true,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = Surface,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 10f)
		};
		split.Panel1.Controls.Add(list);
		split.Panel2.Controls.Add(details);
		layout.Controls.Add(split, 0, 1);

		GroupBox assignmentGroup = new GroupBox
		{
			Dock = DockStyle.Fill,
			Text = "Questionnaire slot assignment",
			ForeColor = TextPrimary,
			BackColor = Surface,
			Padding = new Padding(10)
		};
		TableLayoutPanel assignmentLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 4,
			RowCount = 3,
			BackColor = Surface
		};
		assignmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		assignmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		assignmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		assignmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
		assignmentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		assignmentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		assignmentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		assignmentLayout.Controls.Add(DarkLabel("Insertion slot"), 0, 0);
		ComboBox slotBox = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = InputBackground,
			ForeColor = TextPrimary,
			FlatStyle = FlatStyle.Flat
		};
		assignmentLayout.Controls.Add(slotBox, 1, 0);
		assignmentLayout.Controls.Add(DarkLabel("Questionnaire task"), 2, 0);
		ComboBox taskBox = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			BackColor = InputBackground,
			ForeColor = TextPrimary,
			FlatStyle = FlatStyle.Flat
		};
		foreach (object item in _questionnaireList.Items)
		{
			if (item is QuestionnaireTaskItem)
				taskBox.Items.Add(item);
		}
		assignmentLayout.Controls.Add(taskBox, 3, 0);

		FlowLayoutPanel assignmentActions = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 6, 0, 0),
			BackColor = Surface
		};
		Button assignButton = new Button { Text = "Assign and publish", AutoSize = true, Height = 32 };
		StylePrimaryButton(assignButton);
		assignmentActions.Controls.Add(assignButton);
		Button removeAssignmentButton = new Button { Text = "Remove assignment", AutoSize = true, Height = 32 };
		StyleSecondaryButton(removeAssignmentButton);
		assignmentActions.Controls.Add(removeAssignmentButton);
		assignmentLayout.Controls.Add(assignmentActions, 0, 1);
		assignmentLayout.SetColumnSpan(assignmentActions, 4);
		Label assignmentStatus = new Label
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			ForeColor = TextMuted,
			Padding = new Padding(0, 5, 0, 0),
			Text = "Select a scene and insertion slot."
		};
		assignmentLayout.Controls.Add(assignmentStatus, 0, 2);
		assignmentLayout.SetColumnSpan(assignmentStatus, 4);
		assignmentGroup.Controls.Add(assignmentLayout);
		layout.Controls.Add(assignmentGroup, 0, 2);

		FlowLayoutPanel actions = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.RightToLeft,
			Padding = new Padding(0, 10, 0, 0),
			BackColor = Surface
		};
		Button closeButton = new Button { Text = "Close", AutoSize = true, Height = 32 };
		StyleSecondaryButton(closeButton);
		closeButton.Click += delegate { dialog.Close(); };
		actions.Controls.Add(closeButton);
		Button refreshButton = new Button { Text = "Refresh", AutoSize = true, Height = 32 };
		StyleSecondaryButton(refreshButton);
		actions.Controls.Add(refreshButton);
		layout.Controls.Add(actions, 0, 3);
		dialog.Controls.Add(layout);

		void UpdateAssignmentControls()
		{
			UnitySceneInfo? scene = list.SelectedItem as UnitySceneInfo;
			UnityQuestionnaireSlotInfo? slot = slotBox.SelectedItem as UnityQuestionnaireSlotInfo;
			QuestionnaireTaskItem? task = taskBox.SelectedItem as QuestionnaireTaskItem;
			assignButton.Enabled = scene != null && slot != null && task != null;
			removeAssignmentButton.Enabled = false;
			if (scene == null)
			{
				assignmentStatus.Text = "Select a scene.";
				return;
			}
			if (scene.QuestionnaireSlots.Count == 0)
			{
				assignmentStatus.Text = "This scene does not declare questionnaire slots.";
				return;
			}
			if (slot == null)
			{
				assignmentStatus.Text = "Select an insertion slot.";
				return;
			}
			SceneQuestionnaireAssignment? assignment = _configurator.ReadSceneSchedule(scene.Name).assignments
				.FirstOrDefault(item => string.Equals(item.slotId, slot.SlotId, StringComparison.OrdinalIgnoreCase));
			if (assignment == null)
			{
				assignmentStatus.Text = taskBox.Items.Count == 0
					? "No questionnaire tasks are available. Generate a questionnaire in the main Agent window first."
					: $"{slot.SlotId} is empty. Choose a questionnaire task, then publish.";
				return;
			}
			removeAssignmentButton.Enabled = true;
			string name = !string.IsNullOrWhiteSpace(assignment.instrumentName)
				? assignment.instrumentName
				: !string.IsNullOrWhiteSpace(assignment.construct) ? assignment.construct : assignment.questionnaireId;
			assignmentStatus.Text = $"Published: {slot.SlotId} -> {name} ({assignment.scale}-point {assignment.responseMode})";
		}

		void SelectScene(UnitySceneInfo? scene)
		{
			details.Text = scene?.Details() ?? "Select a scene.";
			slotBox.BeginUpdate();
			try
			{
				slotBox.Items.Clear();
				if (scene != null)
				{
					foreach (UnityQuestionnaireSlotInfo slot in scene.QuestionnaireSlots)
						slotBox.Items.Add(slot);
				}
				if (slotBox.Items.Count > 0)
					slotBox.SelectedIndex = 0;
			}
			finally
			{
				slotBox.EndUpdate();
			}
			UpdateAssignmentControls();
		}

		list.SelectedIndexChanged += delegate
		{
			SelectScene(list.SelectedItem as UnitySceneInfo);
		};
		slotBox.SelectedIndexChanged += delegate { UpdateAssignmentControls(); };
		taskBox.SelectedIndexChanged += delegate { UpdateAssignmentControls(); };

		void LoadScenes(string preferredScenePath = "", string preferredSlotId = "")
		{
			list.BeginUpdate();
			try
			{
				string selectedPath = !string.IsNullOrWhiteSpace(preferredScenePath)
					? preferredScenePath
					: (list.SelectedItem as UnitySceneInfo)?.RelativePath ?? "";
				list.Items.Clear();
				IReadOnlyList<UnitySceneInfo> scenes = _configurator.DiscoverScenes();
				foreach (UnitySceneInfo scene in scenes)
				{
					list.Items.Add(scene);
				}
				details.Text = list.Items.Count == 0
					? "No .unity scenes were found under Assets/Scenes."
					: "Select a scene to inspect its role, Build Settings status, questionnaire bank, and insertion points.";
				if (list.Items.Count > 0)
				{
					int selectedIndex = Enumerable.Range(0, list.Items.Count)
						.Where(index => string.Equals((list.Items[index] as UnitySceneInfo)?.RelativePath, selectedPath, StringComparison.OrdinalIgnoreCase))
						.DefaultIfEmpty(-1)
						.First();
					if (selectedIndex < 0)
						selectedIndex = Enumerable.Range(0, list.Items.Count)
							.FirstOrDefault(index => (list.Items[index] as UnitySceneInfo)?.IsBuildEnabled == true);
					list.SelectedIndex = selectedIndex;
					if (!string.IsNullOrWhiteSpace(preferredSlotId))
					{
						for (int index = 0; index < slotBox.Items.Count; index++)
						{
							if (slotBox.Items[index] is UnityQuestionnaireSlotInfo slot &&
								string.Equals(slot.SlotId, preferredSlotId, StringComparison.OrdinalIgnoreCase))
							{
								slotBox.SelectedIndex = index;
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				details.Text = "Could not read Unity scenes: " + ex.Message;
			}
			finally
			{
				list.EndUpdate();
			}
		}

		assignButton.Click += delegate
		{
			UnitySceneInfo? scene = list.SelectedItem as UnitySceneInfo;
			UnityQuestionnaireSlotInfo? slot = slotBox.SelectedItem as UnityQuestionnaireSlotInfo;
			QuestionnaireTaskItem? task = taskBox.SelectedItem as QuestionnaireTaskItem;
			if (scene == null || slot == null || task == null)
				return;
			DialogResult confirmation = MessageBox.Show(
				dialog,
				$"Publish '{task.Display}' to scene '{scene.Name}', slot '{slot.SlotId}'?\n\n" +
				"This writes a scene-specific question bank and schedule. Shared Scale.json will not be changed.",
				"Confirm questionnaire assignment",
				MessageBoxButtons.OKCancel,
				MessageBoxIcon.Question);
			if (confirmation != DialogResult.OK)
				return;
			try
			{
				string report = _configurator.AssignQuestionnaireToSlot(scene, slot, CloneConfig(task.Config));
				AppendChat("Agent", report);
				LoadScenes(scene.RelativePath, slot.SlotId);
				RefreshScenes(announce: false);
			}
			catch (Exception ex)
			{
				MessageBox.Show(dialog, ex.Message, "Assignment failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		};

		removeAssignmentButton.Click += delegate
		{
			UnitySceneInfo? scene = list.SelectedItem as UnitySceneInfo;
			UnityQuestionnaireSlotInfo? slot = slotBox.SelectedItem as UnityQuestionnaireSlotInfo;
			if (scene == null || slot == null)
				return;
			DialogResult confirmation = MessageBox.Show(
				dialog,
				$"Remove the published questionnaire assignment from '{scene.Name}' / '{slot.SlotId}'?",
				"Confirm removal",
				MessageBoxButtons.OKCancel,
				MessageBoxIcon.Warning);
			if (confirmation != DialogResult.OK)
				return;
			string report = _configurator.RemoveQuestionnaireFromSlot(scene, slot);
			AppendChat("Agent", report);
			LoadScenes(scene.RelativePath, slot.SlotId);
			RefreshScenes(announce: false);
		};

		refreshButton.Click += delegate
		{
			LoadScenes();
			RefreshScenes(announce: false);
		};
		LoadScenes();
		dialog.ShowDialog(this);
	}

	private Control BuildQuestionnaireListPanel()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 3,
			ColumnCount = 1,
			BackColor = Surface,
			RowStyles = 
			{
				new RowStyle(SizeType.Percent, 100f),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize)
			}
		};
		_questionnaireList = new ListBox
		{
			Dock = DockStyle.Fill,
			IntegralHeight = false,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = SurfaceAlt,
			ForeColor = TextPrimary,
			Font = new Font("Segoe UI", 9.5f)
		};
		_questionnaireList.SelectedIndexChanged += delegate
		{
			ShowSelectedQuestionnaireTaskPreview();
			UpdateQuestionnaireTaskStatus();
		};
		obj.Controls.Add(_questionnaireList, 0, 0);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 4, 0, 0),
			BackColor = Surface
		};
		_removeQuestionnaireButton = new Button
		{
			Text = "Delete selected",
			AutoSize = true,
			Height = 30
		};
		StyleSecondaryButton(_removeQuestionnaireButton);
		_removeQuestionnaireButton.Click += delegate
		{
			RemoveSelectedQuestionnaireTask(manual: true);
		};
		flowLayoutPanel.Controls.Add(_removeQuestionnaireButton);
		_clearQuestionnairesButton = new Button
		{
			Text = "Clear list",
			AutoSize = true,
			Height = 30
		};
		StyleSecondaryButton(_clearQuestionnairesButton);
		_clearQuestionnairesButton.Click += delegate
		{
			ClearQuestionnaireTasks(manual: true);
		};
		flowLayoutPanel.Controls.Add(_clearQuestionnairesButton);
		obj.Controls.Add(flowLayoutPanel, 0, 1);
		_questionnaireListStatusLabel = new Label
		{
			AutoSize = true,
			ForeColor = TextMuted,
			Padding = new Padding(0, 4, 0, 0)
		};
		obj.Controls.Add(_questionnaireListStatusLabel, 0, 2);
		UpdateQuestionnaireTaskStatus();
		return obj;
	}

	private Control BuildExportSettingsPanel()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 4,
			ColumnCount = 1,
			BackColor = Surface
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = Surface
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.Controls.Add(DarkLabel("Folder"), 0, 0);
		_exportFolderBox = CreateSettingsTextBox(StudyPaths.DefaultOutputFolder);
		_exportFolderBox.TextChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel2.Controls.Add(_exportFolderBox, 1, 0);
		Button button = new Button
		{
			Text = "Browse",
			AutoSize = true,
			Height = 28
		};
		StyleSecondaryButton(button);
		button.Click += delegate
		{
			BrowseExportFolder();
		};
		tableLayoutPanel2.Controls.Add(button, 2, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 4,
			RowCount = 1,
			Padding = new Padding(0, 6, 0, 0),
			BackColor = Surface
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
		tableLayoutPanel3.Controls.Add(DarkLabel("Subfolder"), 0, 0);
		_exportSubfolderBox = CreateSettingsTextBox("ExportsCSV");
		_exportSubfolderBox.TextChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel3.Controls.Add(_exportSubfolderBox, 1, 0);
		tableLayoutPanel3.Controls.Add(DarkLabel("Prefix"), 2, 0);
		_fileNamePrefixBox = CreateSettingsTextBox("PAXSM");
		_fileNamePrefixBox.TextChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel3.Controls.Add(_fileNamePrefixBox, 3, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 1);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 6,
			RowCount = 1,
			Padding = new Padding(0, 6, 0, 0),
			BackColor = Surface
		};
		for (int num = 0; num < 6; num++)
		{
			tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle((num % 2 != 0) ? SizeType.Percent : SizeType.AutoSize, (num % 2 == 0) ? 0f : 33.33f));
		}
		tableLayoutPanel4.Controls.Add(DarkLabel("P"), 0, 0);
		_participantNumberBox = CreateSettingsNumberBox(1);
		_participantNumberBox.ValueChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel4.Controls.Add(_participantNumberBox, 1, 0);
		tableLayoutPanel4.Controls.Add(DarkLabel("Session"), 2, 0);
		_sessionNumberBox = CreateSettingsNumberBox(1);
		_sessionNumberBox.ValueChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel4.Controls.Add(_sessionNumberBox, 3, 0);
		tableLayoutPanel4.Controls.Add(DarkLabel("Condition #"), 4, 0);
		_conditionIndexBox = CreateSettingsNumberBox(1);
		_conditionIndexBox.ValueChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel4.Controls.Add(_conditionIndexBox, 5, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 0, 2);
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(0, 6, 0, 0),
			BackColor = Surface
		};
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel5.Controls.Add(DarkLabel("Condition label"), 0, 0);
		_conditionLabelBox = CreateSettingsTextBox("QuestionnaireOnly");
		_conditionLabelBox.TextChanged += delegate
		{
			ExportSettingChanged();
		};
		tableLayoutPanel5.Controls.Add(_conditionLabelBox, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel5, 0, 3);
		return tableLayoutPanel;
	}

	private static TextBox CreateSettingsTextBox(string value)
	{
		return new TextBox
		{
			Dock = DockStyle.Fill,
			Text = value,
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = InputBackground,
			ForeColor = TextPrimary
		};
	}

	private static NumericUpDown CreateSettingsNumberBox(int value)
	{
		return new NumericUpDown
		{
			Dock = DockStyle.Fill,
			Minimum = 1m,
			Maximum = 999999m,
			Value = Math.Max(1, value),
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = InputBackground,
			ForeColor = TextPrimary
		};
	}

	private void LocateProjectRoot()
	{
		if (_configurator.TryLocateProjectRoot(AppContext.BaseDirectory) || _configurator.TryLocateProjectRoot(Environment.CurrentDirectory))
		{
			_projectRootBox.Text = _configurator.ProjectRoot;
			RefreshScenes(announce: false);
			return;
		}
		_projectRootBox.Text = "(not found)";
		AppendChat("Agent", "I could not locate the Unity project root automatically. Use Browse and select the folder that contains Assets, Packages, and ProjectSettings.");
	}

	private void SeedConversation()
	{
		AppendChat("Agent", "Tell me what the questionnaire should measure. Example: I want to compare hard and easy tasks and capture how users feel while solving them.");
		AppendChat("Agent", "Recommended questionnaires are saved into the task list on the right. Select one task and click Confirm and apply to configure it.");
		AppendChat("Agent", "After selecting a task, you can edit it in chat with commands like 'change this to 7 points', 'make it card', or 'switch this to SUS'.");
		AppendChat("Agent", "You can remove tasks manually, or type things like 'delete SUS' / '删除可用性问卷'. Files are written only after Confirm and apply.");
	}

	private async Task GenerateConfigAsync()
	{
		string userText = _promptBox.Text.Trim();
		if (string.IsNullOrWhiteSpace(userText))
		{
			MessageBox.Show(this, "Please describe the study first.", "PAXSM Study Agent", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			if (TryHandleQuestionnaireTaskCommand(userText) || TryHandleSelectedTaskEditCommand(userText))
			{
				return;
			}
			if (!HasRequiredLlmConnection())
			{
				_currentConfig = null;
				_previewReadyForApply = false;
				_lastPreviewRequest = "";
				_previewBox.Text = BuildMissingConnectionMessage();
				_jsonPreviewBox.Clear();
				MessageBox.Show(this, BuildMissingConnectionMessage(), "PAXSM Study Agent", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			await RunBusyAsync("Generating configuration...", async delegate
			{
				string request = (string.IsNullOrWhiteSpace(_pendingRequest) ? userText : (_pendingRequest + Environment.NewLine + Environment.NewLine + "Researcher clarification: " + userText));
				AppendChat(string.IsNullOrWhiteSpace(_pendingRequest) ? "Researcher" : "Researcher clarification", userText);
				StartThinkingIndicator();
				_previewBox.Text = "Thinking...\r\n\r\nThe language model is generating the formal preview. Nothing has been written.";
				_jsonPreviewBox.Clear();
				AgentResult agentResult;
				try
				{
					await Task.Delay(75, _disposeCts.Token);
					string selectedProvider = SelectedProvider();
					string connectionValue = _apiKeyBox.Text;
					string selectedModel = _modelBox.Text;
					agentResult = await Task.Run(async () => await _agentEngine.GenerateAsync(request, selectedProvider, connectionValue, selectedModel, _disposeCts.Token), _disposeCts.Token);
				}
				catch
				{
					StopThinkingIndicator(removeMessage: true);
					throw;
				}
				StopThinkingIndicator(removeMessage: true);
				if (agentResult.NeedsClarification)
				{
					_currentConfig = null;
					_previewReadyForApply = false;
					_lastPreviewRequest = "";
					_pendingRequest = request;
					_previewBox.Text = BuildClarificationPreview(agentResult);
					_jsonPreviewBox.Clear();
					AppendChat("Agent", agentResult.Summary + Environment.NewLine + agentResult.QuestionsAsText());
					foreach (string warning in agentResult.Warnings)
					{
						AppendChat("Agent warning", warning);
					}
					_promptBox.SelectAll();
					return;
				}
				_pendingRequest = "";
				List<StudyConfig> list = ((agentResult.TaskConfigs.Count > 0) ? agentResult.TaskConfigs : new List<StudyConfig> { agentResult.Config });
				QuestionnaireTaskItem questionnaireTaskItem = null;
				foreach (StudyConfig item in list)
				{
					item.naturalLanguageRequest = request;
					QuestionnaireTaskItem questionnaireTaskItem2 = AddOrUpdateQuestionnaireTask(item, agentResult.Summary, select: false);
					if (questionnaireTaskItem == null)
					{
						questionnaireTaskItem = questionnaireTaskItem2;
					}
				}
				if (questionnaireTaskItem != null)
				{
					_questionnaireList.SelectedItem = questionnaireTaskItem;
				}
				_currentConfig = ((questionnaireTaskItem != null) ? CloneConfig(questionnaireTaskItem.Config) : agentResult.Config);
				ApplyExportSettingsToConfig(_currentConfig);
				LoadExportSettingsFromConfig(_currentConfig);
				_previewReadyForApply = true;
				_lastPreviewRequest = request;
				_previewBox.Text = BuildNaturalPreview(_currentConfig, agentResult.Source, agentResult.Warnings, applied: false);
				_jsonPreviewBox.Text = _configurator.PrettyJson(_currentConfig);
				string value = BuildAddedTasksLine(list);
				AppendChat("Agent", $"{agentResult.Summary}{value}\r\nSource: {agentResult.Source}\r\nThis is only a preview. Nothing has been written yet. Click Confirm and apply when this is final.");
				foreach (string warning2 in agentResult.Warnings)
				{
					AppendChat("Agent warning", warning2);
				}
			});
		}
	}

	private async Task ApplyConfigAsync()
	{
		QuestionnaireTaskItem questionnaireTaskItem = SelectedQuestionnaireTask();
		if (questionnaireTaskItem != null)
		{
			_currentConfig = CloneConfig(questionnaireTaskItem.Config);
			ApplyExportSettingsToConfig(_currentConfig);
			_previewReadyForApply = true;
			_lastPreviewRequest = _currentConfig.naturalLanguageRequest;
		}
		if (_currentConfig == null || !_previewReadyForApply)
		{
			MessageBox.Show(this, "Please generate a questionnaire task or select one from the task list first. Nothing will be written until you confirm a selected task.", "PAXSM Study Agent", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		await RunBusyAsync("Applying configuration...", delegate
		{
			ApplyExportSettingsToConfig(_currentConfig);
			string message = _configurator.Apply(_currentConfig);
			AppendChat("Agent", message);
			_previewBox.Text = BuildNaturalPreview(_currentConfig, "Confirmed", Array.Empty<string>(), applied: true);
			_jsonPreviewBox.Text = _configurator.PrettyJson(_currentConfig);
			_previewReadyForApply = false;
			_lastPreviewRequest = "";
			return Task.CompletedTask;
		});
	}

	private void ValidateProject()
	{
		try
		{
			ProjectValidationResult projectValidationResult = _configurator.Validate();
			AppendChat("Validation", projectValidationResult.ToReport());
		}
		catch (Exception ex)
		{
			AppendChat("Validation error", ex.Message);
		}
	}

	private string BuildClarificationPreview(AgentResult result)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("More information needed");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine(result.Summary);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Please answer:");
		stringBuilder.AppendLine(result.QuestionsAsText());
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("No Unity files have been changed.");
		return stringBuilder.ToString();
	}

	private string BuildNaturalPreview(StudyConfig config, string source, IEnumerable<string> warnings, bool applied)
	{
		LikertQuestionBank likertQuestionBank = _agentEngine.BuildQuestionBank(config);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(applied ? "Applied study setup" : "Study setup preview");
		stringBuilder.AppendLine(applied ? "Status: written to the Unity project." : "Status: preview only. Nothing has been written yet.");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Recommended questionnaire setup");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
		handler.AppendLiteral("- Questionnaire type: ");
		handler.AppendFormatted(FormatConstruct(config.construct));
		stringBuilder3.AppendLine(ref handler);
		if (!string.IsNullOrWhiteSpace(config.recommendationRole))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder2);
			handler.AppendLiteral("- Recommendation role: ");
			handler.AppendFormatted(config.recommendationRole);
			stringBuilder4.AppendLine(ref handler);
		}
		if (!string.IsNullOrWhiteSpace(config.instrumentName))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
			handler.AppendLiteral("- Recommended instrument: ");
			handler.AppendFormatted(config.instrumentName);
			stringBuilder5.AppendLine(ref handler);
		}
		if (!string.IsNullOrWhiteSpace(config.instrumentStatus))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder2);
			handler.AppendLiteral("- Instrument status: ");
			handler.AppendFormatted(FormatInstrumentStatus(config.instrumentStatus));
			stringBuilder6.AppendLine(ref handler);
		}
		if (!string.IsNullOrWhiteSpace(config.recommendedStandardInstrument))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(34, 1, stringBuilder2);
			handler.AppendLiteral("- Existing option(s) to cite/use: ");
			handler.AppendFormatted(config.recommendedStandardInstrument);
			stringBuilder7.AppendLine(ref handler);
		}
		if (!string.IsNullOrWhiteSpace(config.recommendationRationale))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("- Why this fits: ");
			handler.AppendFormatted(config.recommendationRationale);
			stringBuilder8.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("- Answer mode: ");
		handler.AppendFormatted(FormatResponseMode(config.responseMode));
		stringBuilder9.AppendLine(ref handler);
		if (config.scale == 21)
		{
			stringBuilder.AppendLine("- Mode rule: 21-point scales are forced to slider mode.");
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("- Scale points: ");
		handler.AppendFormatted(config.scale);
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("- Items: ");
		handler.AppendFormatted(likertQuestionBank.items.Count);
		stringBuilder11.AppendLine(ref handler);
		for (int i = 0; i < likertQuestionBank.items.Count; i++)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
			handler.AppendLiteral("  ");
			handler.AppendFormatted(i + 1);
			handler.AppendLiteral(". ");
			handler.AppendFormatted(likertQuestionBank.items[i].stem);
			stringBuilder12.AppendLine(ref handler);
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("System settings to apply");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("- Merged CSV: ");
		handler.AppendFormatted(YesNo(config.exportMergedCsv));
		stringBuilder13.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder14 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("- Raw stage events: ");
		handler.AppendFormatted(YesNo(config.exportRawStageEvents));
		stringBuilder14.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder15 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("- Export on quit: ");
		handler.AppendFormatted(YesNo(config.exportOnQuit));
		stringBuilder15.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder16 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
		handler.AppendLiteral("- Output folder: ");
		handler.AppendFormatted(config.outputFolder);
		stringBuilder16.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder17 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
		handler.AppendLiteral("- Output subfolder: ");
		handler.AppendFormatted(config.outputSubfolder);
		stringBuilder17.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder18 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("- File prefix: ");
		handler.AppendFormatted(config.fileNamePrefix);
		stringBuilder18.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder19 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 2, stringBuilder2);
		handler.AppendLiteral("- Participant/session: P");
		handler.AppendFormatted(config.participantNumber, "000");
		handler.AppendLiteral(" / S");
		handler.AppendFormatted(config.sessionNumber);
		stringBuilder19.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder20 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 2, stringBuilder2);
		handler.AppendLiteral("- Condition: C");
		handler.AppendFormatted(Math.Max(1, config.conditionIndex));
		handler.AppendLiteral(" / ");
		handler.AppendFormatted(config.conditionLabel);
		stringBuilder20.AppendLine(ref handler);
		stringBuilder.AppendLine("- CSV columns include: participantNumber, sessionNumber, conditionIndex, conditionLabel");
		stringBuilder.AppendLine("- Internal resource paths and version fields are filled automatically.");
		stringBuilder.AppendLine();
		bool flag = false;
		foreach (string warning in warnings)
		{
			if (!flag)
			{
				stringBuilder.AppendLine("Warnings / things to confirm");
				flag = true;
			}
			stringBuilder.AppendLine("- " + warning);
		}
		if (!flag)
		{
			stringBuilder.AppendLine("Warnings / things to confirm");
			stringBuilder.AppendLine("- None from the agent.");
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder21 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("Generated by: ");
		handler.AppendFormatted(source);
		stringBuilder21.AppendLine(ref handler);
		return stringBuilder.ToString();
	}

	private static string FormatConstruct(string construct)
	{
		return construct switch
		{
			"cognitive_load" => "Cognitive load", 
			"usability" => "System usability", 
			"user_experience" => "VR user experience", 
			"physical_demand" => "Physical demand / perceived exertion", 
			"safety_comfort" => "Safety / comfort", 
			"continuance_intention" => "Willingness to continue", 
			"presence" => "Presence / immersion", 
			"presence_spatial" => "Spatial presence", 
			"presence_social" => "Social presence", 
			"collaboration_quality" => "Collaboration / communication quality", 
			"presence_self" => "Self-presence / embodiment", 
			"presence_combined" => "Combined presence", 
			"motion_sickness" => "Motion sickness / cybersickness", 
			"simulator_realism" => "Simulator realism", 
			"immersion" => "Immersion", 
			"embodiment" => "Embodiment", 
			"virtual_therapist_alliance" => "Virtual therapist alliance", 
			"trust" => "Trust", 
			"stress" => "Stress", 
			_ => string.IsNullOrWhiteSpace(construct) ? "Not set" : construct, 
		};
	}

	private static string FormatDesign(string design)
	{
		if (!(design == "within-subjects"))
		{
			if (design == "between-subjects")
			{
				return "Between-subjects / between group";
			}
			return string.IsNullOrWhiteSpace(design) ? "Not set" : design;
		}
		return "Within-subjects / within group";
	}

	private static string FormatResponseMode(string mode)
	{
		if (!(mode == "card"))
		{
			if (mode == "slider")
			{
				return "Slider / knob-style scale";
			}
			return string.IsNullOrWhiteSpace(mode) ? "Not set" : mode;
		}
		return "Card-based response options";
	}

	private static string FormatInstrumentStatus(string status)
	{
		return (status ?? "").Trim().ToLowerInvariant() switch
		{
			"standardized" => "Standardized questionnaire", 
			"standardized_available" => "Standardized questionnaire available in PAXSM", 
			"standardized_recommended_not_deployed" => "Standard questionnaire recommended: import/verify original wording, response scale, and scoring before claiming standardized use", 
			"adapted" => "Adapted from an existing questionnaire family: document wording/response/scoring changes", 
			"adapted_short_form" => "Adapted short form or partial implementation: document changes and do not report as the full validated instrument", 
			"custom" => "Custom PAXSM-generated bank: do not claim it as validated without validation", 
			"custom_generated" => "Custom PAXSM-generated bank: do not claim it as validated without validation", 
			"researcher_supplied_custom" => "Researcher-supplied custom items: supplemental, not a validated standard scale", 
			"researcher_supplied_standardized_candidate" => "Researcher-supplied standardized candidate: verify original wording, scale, and scoring", 
			"unknown_needs_review" => "Needs researcher review before formal reporting", 
			_ => string.IsNullOrWhiteSpace(status) ? "Not specified" : status.Trim(), 
		};
	}

	private static string FormatConditions(List<string> conditions)
	{
		if (conditions != null && conditions.Count != 0)
		{
			return string.Join(" / ", conditions);
		}
		return "Not specified";
	}

	private static string YesNo(bool value)
	{
		if (!value)
		{
			return "No";
		}
		return "Yes";
	}

	private QuestionnaireTaskItem? SelectedQuestionnaireTask()
	{
		return _questionnaireList?.SelectedItem as QuestionnaireTaskItem;
	}

	private QuestionnaireTaskItem AddOrUpdateQuestionnaireTask(StudyConfig config, string summary, bool select = true)
	{
		StudyConfig config2 = CloneConfig(config);
		ApplyExportSettingsToConfig(config2);
		string display = BuildTaskDisplay(config2);
		int num = FindQuestionnaireTaskIndex(config2);
		QuestionnaireTaskItem questionnaireTaskItem = new QuestionnaireTaskItem
		{
			Config = config2,
			Summary = summary,
			Display = display
		};
		if (num >= 0)
		{
			_questionnaireList.Items[num] = questionnaireTaskItem;
		}
		else
		{
			_questionnaireList.Items.Add(questionnaireTaskItem);
		}
		if (select)
		{
			_questionnaireList.SelectedItem = questionnaireTaskItem;
		}
		UpdateQuestionnaireTaskStatus();
		return questionnaireTaskItem;
	}

	private int FindQuestionnaireTaskIndex(StudyConfig config)
	{
		for (int i = 0; i < _questionnaireList.Items.Count; i++)
		{
			if (_questionnaireList.Items[i] is QuestionnaireTaskItem questionnaireTaskItem && string.Equals(questionnaireTaskItem.Config.construct, config.construct, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	private void ShowSelectedQuestionnaireTaskPreview()
	{
		QuestionnaireTaskItem questionnaireTaskItem = SelectedQuestionnaireTask();
		if (questionnaireTaskItem != null)
		{
			_currentConfig = CloneConfig(questionnaireTaskItem.Config);
			LoadExportSettingsFromConfig(_currentConfig);
			_previewReadyForApply = true;
			_lastPreviewRequest = _currentConfig.naturalLanguageRequest;
			_previewBox.Text = BuildNaturalPreview(_currentConfig, "Selected task", Array.Empty<string>(), applied: false);
			_jsonPreviewBox.Text = _configurator.PrettyJson(_currentConfig);
		}
	}

	private void RemoveSelectedQuestionnaireTask(bool manual)
	{
		QuestionnaireTaskItem questionnaireTaskItem = SelectedQuestionnaireTask();
		if (questionnaireTaskItem == null)
		{
			if (manual)
			{
				MessageBox.Show(this, "Select a questionnaire task to delete first.", "PAXSM Study Agent", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			return;
		}
		int selectedIndex = _questionnaireList.SelectedIndex;
		_questionnaireList.Items.RemoveAt(selectedIndex);
		_currentConfig = null;
		_previewReadyForApply = false;
		_lastPreviewRequest = "";
		_previewBox.Clear();
		_jsonPreviewBox.Clear();
		if (_questionnaireList.Items.Count > 0)
		{
			_questionnaireList.SelectedIndex = Math.Min(selectedIndex, _questionnaireList.Items.Count - 1);
		}
		UpdateQuestionnaireTaskStatus();
		AppendChat("Agent", "Removed questionnaire task: " + questionnaireTaskItem.Display);
	}

	private void ClearQuestionnaireTasks(bool manual)
	{
		if (_questionnaireList.Items.Count == 0)
		{
			if (manual)
			{
				MessageBox.Show(this, "The questionnaire task list is already empty.", "PAXSM Study Agent", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			return;
		}
		_questionnaireList.Items.Clear();
		_currentConfig = null;
		_previewReadyForApply = false;
		_lastPreviewRequest = "";
		_previewBox.Clear();
		_jsonPreviewBox.Clear();
		UpdateQuestionnaireTaskStatus();
		AppendChat("Agent", "Cleared the questionnaire task list.");
	}

	private bool TryHandleQuestionnaireTaskCommand(string userText)
	{
		if (LooksLikeSelectedTaskEditInsteadOfDelete(userText))
		{
			return false;
		}
		if (!IsDeleteTaskCommand(userText))
		{
			return false;
		}
		AppendChat("Researcher", userText);
		if (IsClearTaskCommand(userText))
		{
			ClearQuestionnaireTasks(manual: false);
			_promptBox.SelectAll();
			return true;
		}
		if (_questionnaireList.Items.Count == 0)
		{
			AppendChat("Agent", "The questionnaire task list is empty. There is nothing to delete yet.");
			_promptBox.SelectAll();
			return true;
		}
		int num = FindTaskIndexMentionedByText(userText);
		if (num < 0 && _questionnaireList.SelectedIndex >= 0 && ContainsAnyLocal(userText, "this", "selected", "这个", "当前", "选中"))
		{
			num = _questionnaireList.SelectedIndex;
		}
		if (num < 0)
		{
			AppendChat("Agent", "I could not tell which questionnaire task to delete. Select it in the task list and click Delete selected, or say something like '删除可用性问卷' / 'delete SUS'.");
			_promptBox.SelectAll();
			return true;
		}
		_questionnaireList.SelectedIndex = num;
		RemoveSelectedQuestionnaireTask(manual: false);
		_promptBox.SelectAll();
		return true;
	}

	private bool TryHandleSelectedTaskEditCommand(string userText)
	{
		QuestionnaireTaskItem questionnaireTaskItem = SelectedQuestionnaireTask();
		if (questionnaireTaskItem == null)
		{
			return false;
		}
		int num = DetectScaleEdit(userText);
		string text = DetectResponseModeEdit(userText);
		string text2 = DetectConstructEdit(userText);
		if (num <= 0 && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(text2))
		{
			if (ContainsExplicitSelectedTaskReference(userText) && ContainsChangeVerb(userText.ToLowerInvariant()))
			{
				AppendChat("Researcher", userText);
				AppendChat("Agent", "I can edit the selected questionnaire task. Tell me the exact change, for example: 'change this to 7 points', 'make it card', or 'switch this to SUS'.");
				_promptBox.SelectAll();
				return true;
			}
			return false;
		}
		if (!ContainsTaskEditIntent(userText))
		{
			return false;
		}
		AppendChat("Researcher", userText);
		StudyConfig studyConfig = CloneConfig(questionnaireTaskItem.Config);
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		bool flag = false;
		bool flag2 = false;
		bool flag3 = num == 21 && text.Equals("card", StringComparison.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(text2) && !string.Equals(studyConfig.construct, text2, StringComparison.OrdinalIgnoreCase))
		{
			studyConfig.construct = text2;
			list.Add("questionnaire type -> " + FormatConstruct(studyConfig.construct));
			int scale = studyConfig.scale;
			string responseMode = studyConfig.responseMode;
			ApplyConstructDefaultsForEdit(studyConfig, text2, num > 0, !string.IsNullOrWhiteSpace(text));
			if (studyConfig.scale != scale && num <= 0)
			{
				list.Add("scale -> " + studyConfig.scale + " points");
			}
			if (!string.Equals(studyConfig.responseMode, responseMode, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(text))
			{
				list.Add("response mode -> " + studyConfig.responseMode);
			}
		}
		if (num > 0 && studyConfig.scale != num)
		{
			studyConfig.scale = num;
			flag = true;
			list.Add("scale -> " + num + " points");
		}
		if (!string.IsNullOrWhiteSpace(text) && !string.Equals(studyConfig.responseMode, text, StringComparison.OrdinalIgnoreCase))
		{
			studyConfig.responseMode = text;
			flag2 = true;
			list.Add("response mode -> " + text);
		}
		if (flag && !flag2)
		{
			studyConfig.responseMode = ((studyConfig.scale == 21) ? "slider" : "card");
			list.Add("response mode -> " + studyConfig.responseMode);
		}
		if (studyConfig.scale == 21 && !string.Equals(studyConfig.responseMode, "slider", StringComparison.OrdinalIgnoreCase))
		{
			studyConfig.responseMode = "slider";
			list2.Add("21-point scales are locked to slider mode in PAXSM, so I changed this task to slider.");
		}
		else if (flag3)
		{
			list2.Add("21-point scales are locked to slider mode in PAXSM, so I ignored the card request for this task.");
		}
		if (studyConfig.scale < 2)
		{
			studyConfig.scale = 2;
		}
		_agentEngine.RefreshInstrumentMetadata(studyConfig);
		AddConstructScaleWarning(studyConfig, list2);
		ApplyExportSettingsToConfig(studyConfig);
		studyConfig.counterbalancingOrder = "None";
		studyConfig.questionBankResourcesPath = "QuestionBanks/Scale";
		studyConfig.randomizeQuestions = false;
		studyConfig.naturalLanguageRequest = AppendTaskEditRequest(questionnaireTaskItem.Config.naturalLanguageRequest, userText);
		studyConfig.generatedSummary = "Updated selected questionnaire task: " + BuildTaskDisplay(studyConfig) + ".";
		int selectedIndex = _questionnaireList.SelectedIndex;
		QuestionnaireTaskItem questionnaireTaskItem2 = ReplaceQuestionnaireTaskAt(selectedIndex, studyConfig, studyConfig.generatedSummary);
		_currentConfig = CloneConfig(questionnaireTaskItem2.Config);
		_pendingRequest = "";
		_previewReadyForApply = true;
		_lastPreviewRequest = _currentConfig.naturalLanguageRequest;
		_previewBox.Text = BuildNaturalPreview(_currentConfig, "Selected task edit", list2, applied: false);
		_jsonPreviewBox.Text = _configurator.PrettyJson(_currentConfig);
		string text3 = ((list.Count == 0) ? "No visible fields changed." : string.Join("; ", list));
		AppendChat("Agent", "Updated the selected questionnaire task: " + questionnaireTaskItem2.Display + Environment.NewLine + text3 + Environment.NewLine + "This is only a preview. Nothing has been written yet. Click Confirm and apply when this is final.");
		foreach (string item in list2)
		{
			AppendChat("Agent warning", item);
		}
		_promptBox.SelectAll();
		return true;
	}

	private QuestionnaireTaskItem ReplaceQuestionnaireTaskAt(int index, StudyConfig config, string summary)
	{
		StudyConfig config2 = CloneConfig(config);
		QuestionnaireTaskItem questionnaireTaskItem = new QuestionnaireTaskItem
		{
			Config = config2,
			Summary = summary,
			Display = BuildTaskDisplay(config2)
		};
		if (index >= 0 && index < _questionnaireList.Items.Count)
		{
			_questionnaireList.Items[index] = questionnaireTaskItem;
			_questionnaireList.SelectedIndex = index;
		}
		else
		{
			_questionnaireList.Items.Add(questionnaireTaskItem);
			_questionnaireList.SelectedItem = questionnaireTaskItem;
		}
		UpdateQuestionnaireTaskStatus();
		return questionnaireTaskItem;
	}

	private static void ApplyConstructDefaultsForEdit(StudyConfig config, string construct, bool hasExplicitScale, bool hasExplicitMode)
	{
		if (!hasExplicitScale)
		{
			config.scale = DefaultScaleForEditConstruct(construct);
		}
		if (!hasExplicitMode)
		{
			config.responseMode = ((config.scale == 21) ? "slider" : "card");
		}
	}

	private static int DefaultScaleForEditConstruct(string construct)
	{
		return construct switch
		{
			"cognitive_load" => 21, 
			"usability" => 5, 
			"motion_sickness" => 4, 
			_ => 7, 
		};
	}

	private static void AddConstructScaleWarning(StudyConfig config, List<string> warnings)
	{
		if (config.construct == "usability" && config.scale != 5)
		{
			warnings.Add("SUS is standardized as a 5-point questionnaire. I kept your requested scale, so treat this PAXSM bank as adapted/custom and document the change.");
		}
		if (config.construct == "cognitive_load" && config.scale != 21)
		{
			warnings.Add("NASA-TLX is commonly administered with 21-point ratings. I kept your requested scale, so treat this PAXSM workload bank as adapted/custom and document the change.");
		}
		if (config.construct == "motion_sickness" && config.scale != 4)
		{
			warnings.Add("SSQ/VRSQ symptom ratings are usually scored with their own fixed response formats. I kept your requested scale, so document this as an adapted/custom discomfort screen.");
		}
	}

	private static string AppendTaskEditRequest(string existingRequest, string editRequest)
	{
		if (string.IsNullOrWhiteSpace(existingRequest))
		{
			return "Researcher task edit: " + editRequest;
		}
		return existingRequest + Environment.NewLine + Environment.NewLine + "Researcher task edit: " + editRequest;
	}

	private static bool ContainsTaskEditIntent(string text)
	{
		string text2 = text.ToLowerInvariant();
		if (ContainsExplicitSelectedTaskReference(text2))
		{
			return true;
		}
		if (ContainsChangeVerb(text2))
		{
			return LooksLikeDirectTaskEditCommand(text2);
		}
		return false;
	}

	private static bool ContainsChangeVerb(string lower)
	{
		return ContainsAnyLocal(lower, "change", "edit", "update", "set", "switch", "make it", "turn it", "convert", "replace", "instead", "改", "改成", "改为", "变成", "变为", "换", "换成", "设置", "设成", "调成");
	}

	private static bool ContainsExplicitSelectedTaskReference(string text)
	{
		return ContainsAnyLocal(text.ToLowerInvariant(), "selected", "selected task", "selected questionnaire", "current task", "current questionnaire", "this task", "this questionnaire", "this survey", "this scale", "选中", "选中的", "当前问卷", "当前任务", "这个问卷", "这个任务", "这个量表", "这条问卷", "这条任务", "右边这个");
	}

	private static bool LooksLikeDirectTaskEditCommand(string lower)
	{
		if (ContainsAnyLocal(lower, "experiment", "study", "within", "between", "recommend", "suggest", "add another", "实验", "研究", "推荐", "再加", "还有", "还要", "我要做", "我想做", "我想测", "我要测"))
		{
			return false;
		}
		if (Regex.Replace(lower.Trim(), "\\s+", "").Length <= 32)
		{
			return true;
		}
		return Regex.IsMatch(lower, "\\b(change|switch|set|make|convert|replace)\\b.*\\b(21|7|5|4|card|slider|sus|nasa|tlx)\\b", RegexOptions.IgnoreCase);
	}

	private static bool LooksLikeSelectedTaskEditInsteadOfDelete(string text)
	{
		if (!IsDeleteTaskCommand(text))
		{
			return false;
		}
		if (DetectScaleEdit(text) <= 0 && string.IsNullOrWhiteSpace(DetectResponseModeEdit(text)) && string.IsNullOrWhiteSpace(DetectConstructEdit(text)))
		{
			return false;
		}
		string text2 = text.ToLowerInvariant();
		if (ContainsChangeVerb(text2))
		{
			return true;
		}
		if (DetectScaleEdit(text) > 0)
		{
			return !ContainsAnyLocal(text2, "questionnaire", "survey", "task", "问卷", "任务");
		}
		return false;
	}

	private static bool IsShortSettingOnly(string text)
	{
		if (LooksLikeQuestion(text))
		{
			return false;
		}
		return Regex.Replace(text.Trim(), "\\s+", "").Length <= 28;
	}

	private static bool LooksLikeQuestion(string text)
	{
		return ContainsAnyLocal(text.ToLowerInvariant(), "?", "？", "what", "why", "how", "which", "什么", "为什么", "怎么", "哪个");
	}

	private static int DetectScaleEdit(string text)
	{
		string text2 = text.ToLowerInvariant();
		Match match = Regex.Match(text2, "(?<!\\d)(21|7|5|4)(?!\\d)\\s*(?:\\u70b9|point|points|pt|pts)?", RegexOptions.IgnoreCase);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result))
		{
			return result;
		}
		if (ContainsAnyLocal(text2, "二十一点", "二十一"))
		{
			return 21;
		}
		if (ContainsAnyLocal(text2, "七点"))
		{
			return 7;
		}
		if (ContainsAnyLocal(text2, "五点"))
		{
			return 5;
		}
		if (ContainsAnyLocal(text2, "四点"))
		{
			return 4;
		}
		return 0;
	}

	private static string DetectResponseModeEdit(string text)
	{
		string text2 = text.ToLowerInvariant();
		int num = LastIndexOfAnyLocal(text2, "card", "cards", "卡片", "卡片式");
		int num2 = LastIndexOfAnyLocal(text2, "slider", "sliders", "slide", "range", "knob", "滑杠", "滑条", "滑块", "旋钮");
		if (num >= 0 || num2 >= 0)
		{
			if (num < num2)
			{
				return "slider";
			}
			return "card";
		}
		return "";
	}

	private static string DetectConstructEdit(string text)
	{
		string text2 = text.ToLowerInvariant();
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		dictionary["cognitive_load"] = LastIndexOfAnyLocal(text2, "cognitive load", "workload", "nasa", "tlx", "认知负荷", "工作负荷");
		dictionary["usability"] = LastIndexOfAnyLocal(text2, "usability", "usable", "sus", "可用性", "易用性", "好用");
		dictionary["user_experience"] = LastIndexOfAnyLocal(text2, "user experience", "ux", "ueq", "vrsuq", "用户体验", "整体体验");
		dictionary["physical_demand"] = LastIndexOfAnyLocal(text2, "physical demand", "physical burden", "perceived exertion", "borg", "rpe", "fatigue", "upper limb", "rehab training", "身体负担", "身体负荷", "体力负担", "上肢运动", "康复训练");
		dictionary["safety_comfort"] = LastIndexOfAnyLocal(text2, "safety", "safe", "comfort", "pain", "injury risk", "stability", "安全感", "安全", "舒适", "疼痛", "风险", "稳定");
		dictionary["continuance_intention"] = LastIndexOfAnyLocal(text2, "willingness to continue", "continue using", "intention to use", "adoption", "acceptance", "adherence", "tam", "utaut", "愿意继续使用", "继续使用", "使用意愿", "接受度");
		dictionary["motion_sickness"] = LastIndexOfAnyLocal(text2, "motion sickness", "cybersickness", "simulator sickness", "ssq", "vrsq", "nausea", "dizzy", "晕动", "恶心", "眩晕");
		dictionary["presence_social"] = LastIndexOfAnyLocal(text2, "social presence", "co-presence", "copresence", "virtual human", "multi-user", "社交临场", "共在", "社交");
		dictionary["presence_self"] = LastIndexOfAnyLocal(text2, "self-presence", "embodiment", "avatar", "body ownership", "virtual body", "自我临场", "身体所有感", "化身", "虚拟身体");
		dictionary["presence_spatial"] = LastIndexOfAnyLocal(text2, "spatial presence", "ipq", "place illusion", "being there", "空间临场", "在那里");
		dictionary["presence_combined"] = LastIndexOfAnyLocal(text2, "presence", "immersion", "immersive", "沉浸", "临场", "存在感");
		dictionary["simulator_realism"] = LastIndexOfAnyLocal(text2, "simulator realism", "simulation realism", "realism", "fidelity", "模拟真实感", "真实感", "保真度");
		dictionary["virtual_therapist_alliance"] = LastIndexOfAnyLocal(text2, "virtual therapist", "therapist alliance", "virtual coach", "coach", "虚拟治疗师", "治疗联盟", "教练");
		dictionary["trust"] = LastIndexOfAnyLocal(text2, "trust", "reliable", "confidence", "信任", "可靠");
		dictionary["stress"] = LastIndexOfAnyLocal(text2, "stress", "pressure", "anxiety", "relaxation", "压力", "焦虑", "紧张", "放松");
		string result = "";
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
			return "";
		}
		return result;
	}

	private int FindTaskIndexMentionedByText(string text)
	{
		int num = DetectOrdinalIndex(text);
		if (num >= 0 && num < _questionnaireList.Items.Count)
		{
			return num;
		}
		for (int i = 0; i < _questionnaireList.Items.Count; i++)
		{
			if (_questionnaireList.Items[i] is QuestionnaireTaskItem item && TaskMatchesText(item, text))
			{
				return i;
			}
		}
		return -1;
	}

	private static int DetectOrdinalIndex(string text)
	{
		string text2 = text.ToLowerInvariant();
		Match match = Regex.Match(text2, "(?:#|no\\.?\\s*|number\\s*|第\\s*)?(\\d+)");
		if (match.Success && int.TryParse(match.Groups[1].Value, out var result) && result > 0)
		{
			return result - 1;
		}
		if (ContainsAnyLocal(text2, "第一个", "第一", "first"))
		{
			return 0;
		}
		if (ContainsAnyLocal(text2, "第二个", "第二", "second"))
		{
			return 1;
		}
		if (ContainsAnyLocal(text2, "第三个", "第三", "third"))
		{
			return 2;
		}
		if (ContainsAnyLocal(text2, "第四个", "第四", "fourth"))
		{
			return 3;
		}
		if (ContainsAnyLocal(text2, "第五个", "第五", "fifth"))
		{
			return 4;
		}
		return -1;
	}

	private static bool TaskMatchesText(QuestionnaireTaskItem item, string text)
	{
		string text2 = text.ToLowerInvariant();
		string text3 = item.Config.construct.ToLowerInvariant();
		if (text2.Contains(text3))
		{
			return true;
		}
		if (text2.Contains(item.Display.ToLowerInvariant()))
		{
			return true;
		}
		return text3 switch
		{
			"user_experience" => ContainsAnyLocal(text2, "user experience", "ux", "vrsuq", "ueq", "overall experience"), 
			"physical_demand" => ContainsAnyLocal(text2, "physical demand", "physical burden", "perceived exertion", "borg", "rpe", "fatigue", "upper limb", "rehab training", "身体负担", "身体负荷", "体力负担", "上肢运动", "康复训练"), 
			"safety_comfort" => ContainsAnyLocal(text2, "safety", "safe", "comfort", "pain", "injury risk", "stability", "安全感", "安全", "舒适", "疼痛", "风险", "稳定"), 
			"continuance_intention" => ContainsAnyLocal(text2, "willingness to continue", "continue using", "intention to use", "adoption", "acceptance", "adherence", "tam", "utaut", "愿意继续使用", "继续使用", "使用意愿", "接受度"), 
			"presence_spatial" => ContainsAnyLocal(text2, "spatial", "ipq", "place illusion", "being there"), 
			"presence_social" => ContainsAnyLocal(text2, "social", "co-presence", "copresence", "virtual human"), 
			"presence_self" => ContainsAnyLocal(text2, "self-presence", "embodiment", "avatar", "body ownership", "veq"), 
			"presence_combined" => ContainsAnyLocal(text2, "presence", "multimodal", "temple", "combined"), 
			"motion_sickness" => ContainsAnyLocal(text2, "motion sickness", "cybersickness", "simulator sickness", "ssq", "vrsq", "nausea", "dizzy"), 
			"simulator_realism" => ContainsAnyLocal(text2, "simulator realism", "realism", "fidelity", "training realism"), 
			"immersion" => ContainsAnyLocal(text2, "immersion", "immersive", "ieq", "engrossed"), 
			"embodiment" => ContainsAnyLocal(text2, "embodiment", "avatar", "body ownership", "veq"), 
			"virtual_therapist_alliance" => ContainsAnyLocal(text2, "virtual therapist", "therapist alliance", "vtas", "coach"), 
			"cognitive_load" => ContainsAnyLocal(text2, "cognitive", "workload", "nasa", "tlx", "认知", "负荷", "任务难度", "难题"), 
			"usability" => ContainsAnyLocal(text2, "usability", "usable", "sus", "可用", "好用", "易用", "用户体验"), 
			"presence" => ContainsAnyLocal(text2, "presence", "immersion", "沉浸", "临场", "在里面"), 
			"trust" => ContainsAnyLocal(text2, "trust", "信任", "可靠"), 
			"stress" => ContainsAnyLocal(text2, "stress", "压力", "紧张", "焦虑"), 
			_ => ContainsAnyLocal(text2, "custom", "自定义"), 
		};
	}

	private static bool IsDeleteTaskCommand(string text)
	{
		return ContainsAnyLocal(text.ToLowerInvariant(), "delete", "remove", "drop", "clear", "删除", "移除", "去掉", "清空");
	}

	private static bool IsClearTaskCommand(string text)
	{
		return ContainsAnyLocal(text.ToLowerInvariant(), "clear all", "remove all", "delete all", "清空", "全部删除", "全删", "全部移除");
	}

	private void UpdateQuestionnaireTaskStatus()
	{
		if (_questionnaireListStatusLabel != null)
		{
			int num = _questionnaireList?.Items.Count ?? 0;
			if (num == 0)
			{
				_questionnaireListStatusLabel.Text = "No questionnaire tasks yet. Generate a preview to add one.";
				return;
			}
			QuestionnaireTaskItem questionnaireTaskItem = SelectedQuestionnaireTask();
			_questionnaireListStatusLabel.Text = ((questionnaireTaskItem == null) ? $"{num} task(s). Select one to configure." : $"{num} task(s). Selected: {questionnaireTaskItem.Display}.");
		}
	}

	private static string BuildTaskDisplay(StudyConfig config)
	{
		string value = (string.IsNullOrWhiteSpace(config.recommendationRole) ? "Task" : config.recommendationRole.Trim());
		string value2 = (string.IsNullOrWhiteSpace(config.instrumentStatus) ? "" : (" [" + config.instrumentStatus.Trim() + "]"));
		string value3 = (LooksLikeFullSsqTask(config) ? "SSQ full / simulator sickness" : FormatConstruct(config.construct));
		return $"{value}: {value3} / {config.scale}-point {FormatResponseModeShort(config.responseMode)}{value2}";
	}

	private static bool LooksLikeFullSsqTask(StudyConfig config)
	{
		string text = (config.naturalLanguageRequest ?? "").ToLowerInvariant();
		string text2 = (config.instrumentName ?? "").ToLowerInvariant();
		bool flag = text.Contains("ssq", StringComparison.OrdinalIgnoreCase) || text2.Contains("ssq", StringComparison.OrdinalIgnoreCase);
		bool flag2 = ContainsAnyLocal(text, "full", "complete", "full version", "complete version", "完整版", "完整", "全版", "全量表", "全部题项") || text2.Contains("full", StringComparison.OrdinalIgnoreCase) || text2.Contains("16-item", StringComparison.OrdinalIgnoreCase);
		return string.Equals(config.construct, "motion_sickness", StringComparison.OrdinalIgnoreCase) && flag && flag2;
	}

	private static string BuildAddedTasksLine(List<StudyConfig> taskConfigs)
	{
		if (taskConfigs.Count == 0)
		{
			return "";
		}
		List<string> list = new List<string>();
		foreach (StudyConfig taskConfig in taskConfigs)
		{
			list.Add(BuildTaskDisplay(taskConfig));
		}
		return Environment.NewLine + "Added questionnaire task(s): " + string.Join("; ", list);
	}

	private static string FormatResponseModeShort(string mode)
	{
		if (!string.IsNullOrWhiteSpace(mode))
		{
			return mode.Trim().ToLowerInvariant();
		}
		return "response";
	}

	private static StudyConfig CloneConfig(StudyConfig config)
	{
		return new StudyConfig
		{
			schemaVersion = config.schemaVersion,
			studyName = config.studyName,
			studyVersion = config.studyVersion,
			design = config.design,
			construct = config.construct,
			participantId = config.participantId,
			participantNumber = config.participantNumber,
			sessionNumber = config.sessionNumber,
			experimenterId = config.experimenterId,
			conditionIndex = config.conditionIndex,
			conditionLabel = config.conditionLabel,
			conditions = new List<string>(config.conditions ?? new List<string>()),
			counterbalancingOrder = config.counterbalancingOrder,
			randomizeQuestions = config.randomizeQuestions,
			questionBankResourcesPath = config.questionBankResourcesPath,
			responseMode = config.responseMode,
			scale = config.scale,
			recommendationRole = config.recommendationRole,
			instrumentName = config.instrumentName,
			instrumentStatus = config.instrumentStatus,
			recommendedStandardInstrument = config.recommendedStandardInstrument,
			recommendationRationale = config.recommendationRationale,
			outputFolder = config.outputFolder,
			outputSubfolder = config.outputSubfolder,
			fileNamePrefix = config.fileNamePrefix,
			exportMergedCsv = config.exportMergedCsv,
			exportRawStageEvents = config.exportRawStageEvents,
			exportOnQuit = config.exportOnQuit,
			preventOverwrite = config.preventOverwrite,
			naturalLanguageRequest = config.naturalLanguageRequest,
			generatedSummary = config.generatedSummary
		};
	}

	private static bool ContainsAnyLocal(string text, params string[] needles)
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

	private static int LastIndexOfAnyLocal(string text, params string[] needles)
	{
		int num = -1;
		foreach (string value in needles)
		{
			int num2 = text.LastIndexOf(value, StringComparison.OrdinalIgnoreCase);
			if (num2 > num)
			{
				num = num2;
			}
		}
		return num;
	}

	private void BrowseExportFolder()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Select the folder where PAXSM should export participant data.",
			UseDescriptionForTitle = true,
			SelectedPath = (string.IsNullOrWhiteSpace(_exportFolderBox.Text) ? "C:\\" : _exportFolderBox.Text)
		};
		if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
		{
			_exportFolderBox.Text = folderBrowserDialog.SelectedPath;
		}
	}

	private bool ExportSettingsUiReady()
	{
		if (_exportFolderBox != null && _exportSubfolderBox != null && _fileNamePrefixBox != null && _conditionLabelBox != null && _participantNumberBox != null && _sessionNumberBox != null)
		{
			return _conditionIndexBox != null;
		}
		return false;
	}

	private void ExportSettingChanged()
	{
		if (!_loadingExportSettings && ExportSettingsUiReady())
		{
			QuestionnaireTaskItem questionnaireTaskItem = SelectedQuestionnaireTask();
			if (questionnaireTaskItem != null)
			{
				StudyConfig config = CloneConfig(questionnaireTaskItem.Config);
				ApplyExportSettingsToConfig(config);
				int selectedIndex = _questionnaireList.SelectedIndex;
				QuestionnaireTaskItem questionnaireTaskItem2 = ReplaceQuestionnaireTaskAt(selectedIndex, config, questionnaireTaskItem.Summary);
				_currentConfig = CloneConfig(questionnaireTaskItem2.Config);
			}
			else if (_currentConfig != null)
			{
				ApplyExportSettingsToConfig(_currentConfig);
			}
			if (_currentConfig != null)
			{
				_previewReadyForApply = true;
				_lastPreviewRequest = _currentConfig.naturalLanguageRequest;
				_previewBox.Text = BuildNaturalPreview(_currentConfig, "Export settings", Array.Empty<string>(), applied: false);
				_jsonPreviewBox.Text = _configurator.PrettyJson(_currentConfig);
			}
		}
	}

	private void ApplyExportSettingsToConfig(StudyConfig config)
	{
		if (ExportSettingsUiReady())
		{
			config.outputFolder = CleanOrDefault(_exportFolderBox.Text, StudyPaths.DefaultOutputFolder);
			config.outputSubfolder = CleanOrDefault(_exportSubfolderBox.Text, "ExportsCSV");
			config.fileNamePrefix = CleanFileToken(_fileNamePrefixBox.Text, "PAXSM");
			config.participantNumber = Math.Max(1, (int)_participantNumberBox.Value);
			config.participantId = $"P{config.participantNumber:000}";
			config.sessionNumber = Math.Max(1, (int)_sessionNumberBox.Value);
			config.conditionIndex = Math.Max(1, (int)_conditionIndexBox.Value);
			config.conditionLabel = CleanFileToken(_conditionLabelBox.Text, $"Condition{config.conditionIndex:00}");
		}
	}

	private void LoadExportSettingsFromConfig(StudyConfig config)
	{
		if (!ExportSettingsUiReady())
		{
			return;
		}
		_loadingExportSettings = true;
		try
		{
			_exportFolderBox.Text = CleanOrDefault(config.outputFolder, StudyPaths.DefaultOutputFolder);
			_exportSubfolderBox.Text = CleanOrDefault(config.outputSubfolder, "ExportsCSV");
			_fileNamePrefixBox.Text = CleanFileToken(config.fileNamePrefix, "PAXSM");
			_participantNumberBox.Value = ClampForNumberBox(config.participantNumber);
			_sessionNumberBox.Value = ClampForNumberBox(config.sessionNumber);
			_conditionIndexBox.Value = ClampForNumberBox(config.conditionIndex);
			_conditionLabelBox.Text = CleanFileToken(config.conditionLabel, $"Condition{Math.Max(1, config.conditionIndex):00}");
		}
		finally
		{
			_loadingExportSettings = false;
		}
	}

	private static decimal ClampForNumberBox(int value)
	{
		if (value < 1)
		{
			return 1m;
		}
		if (value > 999999)
		{
			return 999999m;
		}
		return value;
	}

	private static string CleanOrDefault(string value, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return fallback;
	}

	private static string CleanFileToken(string value, string fallback)
	{
		string input = (string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
		input = Regex.Replace(input, "[\\\\/:*?\"<>|]+", "_");
		input = Regex.Replace(input, "\\s+", "_");
		if (!string.IsNullOrWhiteSpace(input))
		{
			return input;
		}
		return fallback;
	}

	private void BrowseProjectRoot()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Select the Unity project folder that contains Assets, Packages, and ProjectSettings.",
			UseDescriptionForTitle = true
		};
		if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
		{
			string selectedPath = folderBrowserDialog.SelectedPath;
			if (!Directory.Exists(Path.Combine(selectedPath, "Assets")) || !Directory.Exists(Path.Combine(selectedPath, "ProjectSettings")))
			{
				MessageBox.Show(this, "That folder does not look like a Unity project root.", "PAXSM Study Agent", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			_configurator.SetProjectRoot(selectedPath);
			_projectRootBox.Text = selectedPath;
			RefreshScenes(announce: false);
			AppendChat("Agent", "Unity project root set to: " + selectedPath);
		}
	}

	private async Task RunBusyAsync(string status, Func<Task> action)
	{
		try
		{
			SetBusy(busy: true, status);
			await action();
			_statusLabel.Text = "Ready";
		}
		catch (OperationCanceledException)
		{
			_statusLabel.Text = "Cancelled";
		}
		catch (Exception ex2)
		{
			_statusLabel.Text = "Error";
			AppendChat("Error", ex2.Message);
		}
		finally
		{
			SetBusy(busy: false, _statusLabel.Text);
		}
	}

	private void SetBusy(bool busy, string status)
	{
		_generateButton.Enabled = !busy;
		_applyButton.Enabled = !busy;
		_validateButton.Enabled = !busy;
		_providerBox.Enabled = !busy;
		_apiKeyBox.Enabled = !busy;
		_modelBox.Enabled = !busy;
		_questionnaireList.Enabled = true;
		_removeQuestionnaireButton.Enabled = !busy;
		_clearQuestionnairesButton.Enabled = !busy;
		if (ExportSettingsUiReady())
		{
			_exportFolderBox.Enabled = !busy;
			_exportSubfolderBox.Enabled = !busy;
			_fileNamePrefixBox.Enabled = !busy;
			_conditionLabelBox.Enabled = !busy;
			_participantNumberBox.Enabled = !busy;
			_sessionNumberBox.Enabled = !busy;
			_conditionIndexBox.Enabled = !busy;
		}
		_statusLabel.Text = status;
	}

	private string SelectedProvider()
	{
		return _providerBox.SelectedItem?.ToString() ?? "Ollama";
	}

	private bool IsOllamaProvider()
	{
		return SelectedProvider().Equals("Ollama", StringComparison.OrdinalIgnoreCase);
	}

	private bool HasRequiredLlmConnection()
	{
		return !string.IsNullOrWhiteSpace(_apiKeyBox.Text);
	}

	private string BuildMissingConnectionMessage()
	{
		if (IsOllamaProvider())
		{
			return "Ollama connection required\r\n\r\nStart Ollama locally and keep the local URL filled in, usually http://localhost:11434. Local rule fallback is disabled.";
		}
		return "LLM connection required\r\n\r\nEnter an OpenAI API key before generating a preview. Local rule fallback is disabled.";
	}

	private void UpdateProviderUi()
	{
		if (_apiKeyBox == null || _modelBox == null || _connectionLabel == null)
		{
			return;
		}
		if (IsOllamaProvider())
		{
			_connectionLabel.Text = "LLM connection";
			_apiKeyBox.UseSystemPasswordChar = false;
			_apiKeyBox.PlaceholderText = "http://localhost:11434";
			if (string.IsNullOrWhiteSpace(_apiKeyBox.Text) || LooksLikeApiKey(_apiKeyBox.Text))
			{
				_apiKeyBox.Text = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
			}
			if (string.IsNullOrWhiteSpace(_modelBox.Text) || _modelBox.Text.Equals("gpt-4.1-mini", StringComparison.OrdinalIgnoreCase))
			{
				_modelBox.Text = Environment.GetEnvironmentVariable("PAXSM_OLLAMA_MODEL") ?? "qwen3:14b";
			}
		}
		else
		{
			_connectionLabel.Text = "LLM connection";
			_apiKeyBox.UseSystemPasswordChar = true;
			_apiKeyBox.PlaceholderText = "OpenAI API key";
			if (_apiKeyBox.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || _apiKeyBox.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				_apiKeyBox.Text = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
			}
			if (string.IsNullOrWhiteSpace(_modelBox.Text) || _modelBox.Text.Equals("qwen3:14b", StringComparison.OrdinalIgnoreCase))
			{
				_modelBox.Text = Environment.GetEnvironmentVariable("PAXSM_OPENAI_MODEL") ?? "gpt-4.1-mini";
			}
		}
		UpdateLlmStatus();
	}

	private static bool LooksLikeApiKey(string value)
	{
		string text = value.Trim();
		if (!text.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
		{
			return text.StartsWith("sess-", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void UpdateLlmStatus()
	{
		if (_llmStatusLabel == null)
		{
			return;
		}
		if (IsOllamaProvider())
		{
			if (HasRequiredLlmConnection())
			{
				string value = (string.IsNullOrWhiteSpace(_modelBox.Text) ? "qwen3:14b" : _modelBox.Text.Trim());
				_llmStatusLabel.Text = $"LLM: Local Ollama selected. No API key is needed. Preview calls {_apiKeyBox.Text.Trim()} with model {value}.";
				_llmStatusLabel.ForeColor = Success;
			}
			else
			{
				_llmStatusLabel.Text = "LLM: Ollama URL required. Start Ollama and use http://localhost:11434.";
				_llmStatusLabel.ForeColor = Error;
			}
		}
		else if (HasRequiredLlmConnection())
		{
			_llmStatusLabel.Text = "LLM: API key provided. Generate preview will call the language model.";
			_llmStatusLabel.ForeColor = Success;
		}
		else
		{
			_llmStatusLabel.Text = "LLM: Not connected. API key required; local fallback is disabled.";
			_llmStatusLabel.ForeColor = Error;
		}
	}

	private void InvalidatePreview()
	{
		if (_previewReadyForApply || _currentConfig != null)
		{
			_previewReadyForApply = false;
			_currentConfig = null;
			_lastPreviewRequest = "";
		}
	}

	private void AppendChat(string speaker, string message)
	{
		bool num = speaker.StartsWith("Researcher", StringComparison.OrdinalIgnoreCase);
		bool flag = speaker.Contains("draft", StringComparison.OrdinalIgnoreCase);
		bool flag2 = speaker.Contains("warning", StringComparison.OrdinalIgnoreCase) || speaker.Contains("error", StringComparison.OrdinalIgnoreCase);
		HorizontalAlignment alignment = (num ? HorizontalAlignment.Right : HorizontalAlignment.Left);
		Color textMuted = TextMuted;
		Color backColor = (num ? ResearcherBubble : (flag ? ResearcherBubble : (flag2 ? WarningBubble : AgentBubble)));
		Color textPrimary = TextPrimary;
		if (_chatBox.TextLength > 0)
		{
			AppendChatSegment(Environment.NewLine, HorizontalAlignment.Left, Surface, Surface, bold: false);
		}
		string text = (num ? "Researcher" : speaker);
		AppendChatSegment(text + Environment.NewLine, alignment, textMuted, Surface, bold: true);
		string text2 = "  " + NormalizeChatMessage(message) + "  " + Environment.NewLine;
		AppendChatSegment(text2, alignment, textPrimary, backColor, bold: false);
		AppendChatSegment(Environment.NewLine, HorizontalAlignment.Left, textPrimary, Surface, bold: false);
		_chatBox.SelectionStart = _chatBox.TextLength;
		_chatBox.ScrollToCaret();
	}

	private void StartThinkingIndicator()
	{
		StopThinkingIndicator(removeMessage: true);
		_thinkingFrameIndex = 0;
		_thinkingMessageStart = _chatBox.TextLength;
		if (_chatBox.TextLength > 0)
		{
			AppendChatSegment(Environment.NewLine, HorizontalAlignment.Left, Surface, Surface, bold: false);
		}
		AppendChatSegment("Agent" + Environment.NewLine, HorizontalAlignment.Left, TextMuted, Surface, bold: true);
		(int Start, int Length) tuple = AppendChatSegment(BuildThinkingBubbleText(), HorizontalAlignment.Left, TextPrimary, AgentBubble, bold: false);
		int item = tuple.Start;
		int item2 = tuple.Length;
		_thinkingBubbleStart = item;
		_thinkingBubbleLength = item2;
		AppendChatSegment(Environment.NewLine, HorizontalAlignment.Left, TextPrimary, Surface, bold: false);
		_thinkingMessageLength = _chatBox.TextLength - _thinkingMessageStart;
		_statusLabel.Text = BuildThinkingStatusText();
		_thinkingTimer.Start();
		_chatBox.SelectionStart = _chatBox.TextLength;
		_chatBox.ScrollToCaret();
	}

	private void AdvanceThinkingIndicator()
	{
		if (_thinkingBubbleStart >= 0 && _thinkingBubbleStart < _chatBox.TextLength)
		{
			_thinkingFrameIndex = (_thinkingFrameIndex + 1) % _thinkingFrames.Length;
			string text = BuildThinkingBubbleText();
			if (_thinkingBubbleStart + _thinkingBubbleLength <= _chatBox.TextLength)
			{
				_chatBox.SelectionStart = _thinkingBubbleStart;
				_chatBox.SelectionLength = _thinkingBubbleLength;
				_chatBox.SelectionAlignment = HorizontalAlignment.Left;
				_chatBox.SelectionColor = TextPrimary;
				_chatBox.SelectionBackColor = AgentBubble;
				_chatBox.SelectionFont = new Font(_chatBox.Font, FontStyle.Regular);
				_chatBox.SelectedText = text;
				_thinkingBubbleLength = text.Length;
				_chatBox.SelectionStart = _chatBox.TextLength;
				_chatBox.SelectionLength = 0;
				_chatBox.SelectionBackColor = Surface;
				_chatBox.SelectionColor = TextPrimary;
				_chatBox.SelectionAlignment = HorizontalAlignment.Left;
				_chatBox.SelectionFont = _chatBox.Font;
				_chatBox.ScrollToCaret();
				_statusLabel.Text = BuildThinkingStatusText();
			}
		}
	}

	private void StopThinkingIndicator(bool removeMessage)
	{
		_thinkingTimer.Stop();
		if (removeMessage && _thinkingMessageStart >= 0 && _thinkingMessageStart < _chatBox.TextLength && _thinkingMessageLength > 0)
		{
			int selectionLength = _chatBox.TextLength - _thinkingMessageStart;
			_chatBox.SelectionStart = _thinkingMessageStart;
			_chatBox.SelectionLength = selectionLength;
			_chatBox.SelectedText = "";
			_chatBox.SelectionStart = _chatBox.TextLength;
			_chatBox.SelectionLength = 0;
			_chatBox.ScrollToCaret();
		}
		_thinkingMessageStart = -1;
		_thinkingMessageLength = 0;
		_thinkingBubbleStart = -1;
		_thinkingBubbleLength = 0;
	}

	private string BuildThinkingBubbleText()
	{
		return "  " + _thinkingFrames[_thinkingFrameIndex] + " Thinking...  " + Environment.NewLine;
	}

	private string BuildThinkingStatusText()
	{
		return _thinkingFrames[_thinkingFrameIndex] + " Thinking...";
	}

	private (int Start, int Length) AppendChatSegment(string text, HorizontalAlignment alignment, Color foreColor, Color backColor, bool bold)
	{
		int textLength = _chatBox.TextLength;
		_chatBox.SelectionStart = textLength;
		_chatBox.SelectionLength = 0;
		_chatBox.SelectionAlignment = alignment;
		_chatBox.SelectionColor = foreColor;
		_chatBox.SelectionBackColor = backColor;
		_chatBox.SelectionFont = new Font(_chatBox.Font, bold ? FontStyle.Bold : FontStyle.Regular);
		_chatBox.AppendText(text);
		_chatBox.SelectionBackColor = Surface;
		_chatBox.SelectionColor = TextPrimary;
		_chatBox.SelectionAlignment = HorizontalAlignment.Left;
		_chatBox.SelectionFont = _chatBox.Font;
		return (Start: textLength, Length: text.Length);
	}

	private static string NormalizeChatMessage(string message)
	{
		return (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine + "  ");
	}
}
