using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;

namespace PAXSMResearcherConsole;

internal sealed class MainForm : Form
{
    private static readonly Color Page = Color.FromArgb(244, 247, 249);
    private static readonly Color Surface = Color.White;
    private static readonly Color Ink = Color.FromArgb(28, 39, 49);
    private static readonly Color Muted = Color.FromArgb(91, 106, 119);
    private static readonly Color Line = Color.FromArgb(211, 219, 225);
    private static readonly Color Accent = Color.FromArgb(23, 118, 111);
    private static readonly Color AccentSoft = Color.FromArgb(224, 241, 239);
    private static readonly Color Sidebar = Color.FromArgb(31, 42, 52);
    private static readonly Color SidebarMuted = Color.FromArgb(176, 190, 201);
    private static readonly Color Warning = Color.FromArgb(166, 104, 24);
    private static readonly Color Success = Color.FromArgb(30, 121, 77);
    private static readonly Color Error = Color.FromArgb(172, 53, 53);

    private readonly ProjectServices _project;
    private readonly DataScanner _dataScanner = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Dictionary<string, BlockRowView> _blockViews =
        new(StringComparer.OrdinalIgnoreCase);

    private Panel _entryView = null!;
    private Panel _consoleView = null!;
    private Panel _contentHost = null!;
    private Panel _controlPage = null!;
    private Panel _dataPage = null!;
    private Panel _personalReferencePage = null!;
    private Panel _probePage = null!;
    private Panel _reviewPage = null!;
    private TextBox _participantInput = null!;
    private NumericUpDown _sessionInput = null!;
    private TextBox _outputInput = null!;
    private Label _entryError = null!;
    private Label _headerContext = null!;
    private Label _participantValue = null!;
    private Label _currentSceneValue = null!;
    private Label _currentSceneDetail = null!;
    private Label _calibrationValue = null!;
    private Label _calibrationDetail = null!;
    private Label _pipelineBadge = null!;
    private Label _calibrationProgress = null!;
    private Label _profileTitle = null!;
    private Label _profileDetail = null!;
    private Button _combinedLaunchButton = null!;
    private Button _freezeProfileButton = null!;
    private Label _statusBar = null!;
    private Label _dataFileCount = null!;
    private Label _dataRunCount = null!;
    private Label _dataLatest = null!;
    private Label _dataScope = null!;
    private ListView _fileList = null!;
    private Label _personalReferenceParticipantValue = null!;
    private Label _personalReferenceParticipantDetail = null!;
    private Label _personalReferenceQualityValue = null!;
    private Label _personalReferenceQualityDetail = null!;
    private Label _personalReferenceTrialsValue = null!;
    private Label _personalReferenceTrialsDetail = null!;
    private Label _personalReferenceStatus = null!;
    private Label _personalReferenceSummary = null!;
    private Label _personalReferencePath = null!;
    private TextBox _personalReferenceExplanation = null!;
    private ListView _personalReferenceMetricList = null!;
    private ListView _personalReferenceDistanceList = null!;
    private ComboBox _matrixDimensionFilter = null!;
    private Label _matrixStatus = null!;
    private TextBox _matrixDetail = null!;
    private Button _buildEvidenceButton = null!;
    private readonly Dictionary<(int Row, int Column), Label> _matrixCells = new();
    private Button _sceneControlNav = null!;
    private Button _participantDataNav = null!;
    private Button _personalReferenceNav = null!;
    private Button _probeDefinitionNav = null!;
    private Button _evidenceReviewNav = null!;
    private Label _probePluginValue = null!;
    private Label _probePluginDetail = null!;
    private ListView _probePluginList = null!;

    private ResearchSession? _session;
    private DataSnapshot? _snapshot;
    private readonly PersonalReferenceProfileReader _personalReferenceReader = new();
    private ProbeRuleCardSet _activeProbePlugin = new();
    private string _activeProbePluginPath = "";
    private IReadOnlyList<ContextualResponseRecord> _contextualRecords = Array.Empty<ContextualResponseRecord>();
    private bool _refreshing;
    private bool _initialDpiLayoutApplied;

    public MainForm(string projectRoot)
    {
        _project = new ProjectServices(projectRoot);
        Text = "PAXSM Researcher Console";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 720);
        Size = new Size(1320, 860);
        BackColor = Page;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;

        BuildEntryView();
        BuildConsoleView();
        ShowEntryView();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();
        FormClosed += (_, _) => _refreshTimer.Stop();
        Shown += (_, _) => ApplyInitialDpiLayout();
    }

    private void ApplyInitialDpiLayout()
    {
        if (_initialDpiLayoutApplied)
            return;
        _initialDpiLayoutApplied = true;

        float scale = Math.Max(1f, DeviceDpi / 96f);
        if (scale > 1.001f)
        {
            SuspendLayout();
            Scale(new SizeF(scale, scale));
            foreach (ColumnHeader column in _fileList.Columns)
                column.Width = (int)Math.Round(column.Width * scale);
            ResumeLayout(true);
        }

        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        int screenMargin = Math.Max(12, (int)Math.Round(16 * scale));
        int maximumWidth = Math.Max(960, workingArea.Width - screenMargin * 2);
        int maximumHeight = Math.Max(640, workingArea.Height - screenMargin * 2);
        MinimumSize = new Size(
            Math.Min(MinimumSize.Width, maximumWidth),
            Math.Min(MinimumSize.Height, maximumHeight));
        int fittedWidth = Math.Min(Width, maximumWidth);
        int fittedHeight = Math.Min(Height, maximumHeight);
        Bounds = new Rectangle(
            workingArea.Left + (workingArea.Width - fittedWidth) / 2,
            workingArea.Top + (workingArea.Height - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    private void BuildEntryView()
    {
        _entryView = new Panel { Dock = DockStyle.Fill, BackColor = Page };
        Controls.Add(_entryView);

        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Surface,
            Padding = new Padding(28, 18, 28, 12)
        };
        header.Controls.Add(CreateBrandBlock("PAXSM Researcher Console", "FULLSTUDY-PAXSM207"));
        _entryView.Controls.Add(header);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Page,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 700));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 560));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        _entryView.Controls.Add(layout);

        Panel content = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Page,
            Padding = new Padding(12, 8, 12, 8)
        };
        layout.Controls.Add(content, 1, 1);

        var introduction = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 84,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Page,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        introduction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        introduction.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        introduction.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.Controls.Add(introduction);

        Label title = CreateLabel("Start a participant session", 24F, Ink, FontStyle.Bold);
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleCenter;
        introduction.Controls.Add(title, 0, 0);

        Label subtitle = CreateLabel(
            "Participant context is required before any scene or data control becomes available.",
            10F,
            Muted);
        subtitle.Dock = DockStyle.Fill;
        subtitle.TextAlign = ContentAlignment.MiddleCenter;
        introduction.Controls.Add(subtitle, 0, 1);

        Panel card = CreateCard();
        card.Dock = DockStyle.Bottom;
        card.Height = 414;
        card.Padding = new Padding(26, 22, 26, 20);
        content.Controls.Add(card);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = Surface
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(form);

        Label participantFieldLabel = CreateFieldLabel("Participant ID");
        form.Controls.Add(participantFieldLabel, 0, 0);
        form.Controls.Add(CreateFieldLabel("Session"), 1, 0);

        _participantInput = CreateTextBox("e.g. P888");
        _participantInput.AccessibleName = "Participant ID";
        form.Controls.Add(_participantInput, 0, 1);
        _sessionInput = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = 999,
            Value = 1,
            Font = new Font(Font.FontFamily, 11F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink
        };
        form.Controls.Add(_sessionInput, 1, 1);

        Label participantHelp = CreateLabel(
            "Required · letters, numbers, hyphens, and underscores only",
            8.8F,
            Muted);
        participantHelp.Dock = DockStyle.Fill;
        participantHelp.TextAlign = ContentAlignment.MiddleLeft;
        form.Controls.Add(participantHelp, 0, 2);
        form.SetColumnSpan(participantHelp, 2);

        Label outputFieldLabel = CreateFieldLabel("Data output root");
        form.Controls.Add(outputFieldLabel, 0, 3);
        form.SetColumnSpan(outputFieldLabel, 2);

        _outputInput = CreateTextBox();
        _outputInput.Text = _project.DefaultOutputRoot;
        form.Controls.Add(_outputInput, 0, 4);
        Button browse = CreateSecondaryButton("Browse…");
        browse.Margin = new Padding(10, 0, 0, 0);
        browse.Click += BrowseOutputRoot;
        form.Controls.Add(browse, 1, 4);

        Panel checks = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 251),
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0, 8, 0, 4)
        };
        form.Controls.Add(checks, 0, 6);
        form.SetColumnSpan(checks, 2);
        checks.Controls.Add(CreateReadinessRow("Overwrite protection", "Enabled"));
        checks.Controls.Add(CreateReadinessRow("Unity scene catalogue", "5 scenes ready"));
        checks.Controls.Add(CreateReadinessRow("Study protocol", "Loaded"));

        _entryError = CreateLabel("", 9F, Error);
        _entryError.Dock = DockStyle.Fill;
        _entryError.TextAlign = ContentAlignment.MiddleLeft;
        form.Controls.Add(_entryError, 0, 7);
        form.SetColumnSpan(_entryError, 2);

        Panel actions = new() { Dock = DockStyle.Fill, BackColor = Surface };
        form.Controls.Add(actions, 0, 8);
        form.SetColumnSpan(actions, 2);
        Button enter = CreatePrimaryButton("Enter experiment control", 226);
        enter.Dock = DockStyle.Right;
        enter.Click += EnterSession;
        actions.Controls.Add(enter);
        AcceptButton = enter;
    }

    private void BuildConsoleView()
    {
        _consoleView = new Panel { Dock = DockStyle.Fill, BackColor = Page, Visible = false };
        Controls.Add(_consoleView);

        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Surface,
            Padding = new Padding(24, 14, 24, 10)
        };
        header.Controls.Add(CreateBrandBlock("PAXSM Researcher Console", "FULLSTUDY-PAXSM207"));
        _headerContext = CreateBadge("No participant", AccentSoft, Accent);
        _headerContext.AutoSize = false;
        _headerContext.Width = 190;
        _headerContext.Height = 34;
        _headerContext.Dock = DockStyle.Right;
        _headerContext.TextAlign = ContentAlignment.MiddleCenter;
        header.Controls.Add(_headerContext);
        Button endSession = CreateGhostButton("End session", 112);
        endSession.Dock = DockStyle.Right;
        endSession.Margin = new Padding(0, 0, 12, 0);
        endSession.Click += (_, _) => EndSession();
        header.Controls.Add(endSession);
        _consoleView.Controls.Add(header);

        Panel sidebar = new()
        {
            Dock = DockStyle.Left,
            Width = 228,
            BackColor = Sidebar,
            Padding = new Padding(14, 18, 14, 14)
        };
        _consoleView.Controls.Add(sidebar);
        BuildSidebar(sidebar);

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Page,
            Padding = new Padding(26, 20, 26, 38)
        };
        _consoleView.Controls.Add(_contentHost);
        _contentHost.BringToFront();

        _statusBar = CreateLabel("Ready.", 9F, Muted);
        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.Height = 30;
        _statusBar.Padding = new Padding(12, 0, 0, 0);
        _statusBar.TextAlign = ContentAlignment.MiddleLeft;
        _statusBar.BackColor = Color.FromArgb(235, 239, 242);
        _consoleView.Controls.Add(_statusBar);
        _statusBar.BringToFront();

        _controlPage = BuildControlPage();
        _dataPage = BuildDataPage();
        _personalReferencePage = BuildPersonalReferencePage();
        _probePage = BuildProbeDefinitionPage();
        _reviewPage = BuildReviewPage();
        _contentHost.Controls.Add(_reviewPage);
        _contentHost.Controls.Add(_probePage);
        _contentHost.Controls.Add(_personalReferencePage);
        _contentHost.Controls.Add(_dataPage);
        _contentHost.Controls.Add(_controlPage);
        ShowConsolePage(_controlPage, _sceneControlNav);
    }

    private void BuildSidebar(Panel sidebar)
    {
        Label participantGroup = CreateLabel("PARTICIPANT RUN", 8.5F, SidebarMuted, FontStyle.Bold);
        participantGroup.Dock = DockStyle.Top;
        participantGroup.Height = 28;
        participantGroup.Padding = new Padding(8, 0, 0, 0);
        sidebar.Controls.Add(participantGroup);

        _sceneControlNav = CreateNavigationButton("Scene control");
        _sceneControlNav.Click += (_, _) => ShowConsolePage(_controlPage, _sceneControlNav);
        sidebar.Controls.Add(_sceneControlNav);
        _sceneControlNav.BringToFront();

        _personalReferenceNav = CreateNavigationButton("Personal reference");
        _personalReferenceNav.Click += async (_, _) =>
        {
            ShowConsolePage(_personalReferencePage, _personalReferenceNav);
            await RefreshDataAsync();
        };
        sidebar.Controls.Add(_personalReferenceNav);
        _personalReferenceNav.BringToFront();

        _participantDataNav = CreateNavigationButton("Participant data");
        _participantDataNav.Click += async (_, _) =>
        {
            ShowConsolePage(_dataPage, _participantDataNav);
            await RefreshDataAsync();
        };
        sidebar.Controls.Add(_participantDataNav);
        _participantDataNav.BringToFront();

        _probeDefinitionNav = CreateNavigationButton("Probe plugins");
        _probeDefinitionNav.Click += (_, _) =>
        {
            RefreshProbePluginSummary();
            ShowConsolePage(_probePage, _probeDefinitionNav);
        };
        sidebar.Controls.Add(_probeDefinitionNav);
        _probeDefinitionNav.BringToFront();

        _evidenceReviewNav = CreateNavigationButton("Evidence review");
        _evidenceReviewNav.Click += (_, _) => ShowConsolePage(_reviewPage, _evidenceReviewNav);
        sidebar.Controls.Add(_evidenceReviewNav);
        _evidenceReviewNav.BringToFront();

        Label configurationGroup = CreateLabel("STUDY CONFIGURATION", 8.5F, SidebarMuted, FontStyle.Bold);
        configurationGroup.Dock = DockStyle.Top;
        configurationGroup.Height = 48;
        configurationGroup.Padding = new Padding(8, 20, 0, 0);
        sidebar.Controls.Add(configurationGroup);
        configurationGroup.BringToFront();

        Button agent = CreateNavigationButton("Questionnaire Agent   LLM");
        agent.Click += (_, _) =>
        {
            _project.TryOpenQuestionnaireAgent(out string message);
            SetStatus(message);
        };
        sidebar.Controls.Add(agent);
        agent.BringToFront();

        Button sceneAssignment = CreateNavigationButton("Scene assignment");
        sceneAssignment.Click += (_, _) => ShowFirstVersionNotice(
            "Scene assignment remains read-only in this first version. Existing Unity scene assignments are unchanged.");
        sidebar.Controls.Add(sceneAssignment);
        sceneAssignment.BringToFront();

        Label projectPath = CreateLabel(_project.ProjectRoot, 8F, SidebarMuted);
        projectPath.Dock = DockStyle.Bottom;
        projectPath.Height = 54;
        projectPath.Padding = new Padding(8, 0, 8, 0);
        projectPath.AutoEllipsis = true;
        sidebar.Controls.Add(projectPath);
    }

    private Panel BuildControlPage()
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = Page, AutoScroll = true };
        Panel body = new() { Dock = DockStyle.Top, Height = 1135, BackColor = Page };
        page.Controls.Add(body);

        Label title = CreateLabel("Scene control", 22F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 0);
        title.Size = new Size(430, 34);
        body.Controls.Add(title);
        Label subtitle = CreateLabel(
            "Launch scenes once and monitor their internal stages without changing the Unity experiment logic.",
            9.5F,
            Muted);
        subtitle.Location = new Point(0, 36);
        subtitle.Size = new Size(760, 28);
        body.Controls.Add(subtitle);
        _pipelineBadge = CreateBadge("Pipeline ready", AccentSoft, Accent);
        _pipelineBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _pipelineBadge.Location = new Point(910, 6);
        _pipelineBadge.Size = new Size(150, 30);
        body.Controls.Add(_pipelineBadge);
        body.Resize += (_, _) => _pipelineBadge.Left = Math.Max(
            subtitle.Right + 12,
            body.ClientSize.Width - _pipelineBadge.Width);

        TableLayoutPanel summaries = new()
        {
            Location = new Point(0, 76),
            Height = 116,
            Width = 1060,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 3,
            BackColor = Page
        };
        summaries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        summaries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        summaries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        body.Controls.Add(summaries);
        body.Resize += (_, _) => summaries.Width = body.ClientSize.Width;

        (Panel participantCard, _participantValue, _) = CreateStatCard("Participant context", "—", "Session — · locked");
        (Panel sceneCard, _currentSceneValue, _currentSceneDetail) = CreateStatCard("Current scene", "None", "Waiting for researcher");
        (Panel calibrationCard, _calibrationValue, _calibrationDetail) = CreateStatCard("Task Probe calibration", "0 / 4", "Not started");
        participantCard.Margin = new Padding(0, 0, 8, 0);
        sceneCard.Margin = new Padding(4, 0, 4, 0);
        calibrationCard.Margin = new Padding(8, 0, 0, 0);
        summaries.Controls.Add(participantCard, 0, 0);
        summaries.Controls.Add(sceneCard, 1, 0);
        summaries.Controls.Add(calibrationCard, 2, 0);

        Label scenesTitle = CreateLabel("Experiment scenes", 13F, Ink, FontStyle.Bold);
        scenesTitle.Location = new Point(0, 214);
        scenesTitle.Size = new Size(260, 28);
        body.Controls.Add(scenesTitle);
        Label scenesHelp = CreateLabel("Only scene-level launch controls are exposed.", 9F, Muted);
        scenesHelp.Location = new Point(0, 242);
        scenesHelp.Size = new Size(500, 24);
        body.Controls.Add(scenesHelp);

        Panel sceneList = new()
        {
            Location = new Point(0, 270),
            Width = 1060,
            Height = 324,
            BackColor = Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(16, 2, 16, 2)
        };
        body.Controls.Add(sceneList);
        body.Resize += (_, _) => sceneList.Width = body.ClientSize.Width;
        AddSceneRow(sceneList, SceneDefinitions.Comparison, 0, false);
        AddSceneRow(sceneList, SceneDefinitions.ResponseCalibration, 80, true);
        AddSceneRow(sceneList, SceneDefinitions.Workload, 160, true);
        AddSceneRow(sceneList, SceneDefinitions.Combined, 240, false);

        Label blockTitle = CreateLabel("Workload scene · internal block monitor", 13F, Ink, FontStyle.Bold);
        blockTitle.Location = new Point(0, 618);
        blockTitle.Size = new Size(480, 28);
        body.Controls.Add(blockTitle);
        Label blockHelp = CreateLabel(
            "Baseline, Mental, Physical, and Temporal rows belong to one XRWorkloadProbeScene. Status is read from Unity questionnaire checkpoints.",
            9F,
            Muted);
        blockHelp.Location = new Point(0, 646);
        blockHelp.Size = new Size(760, 24);
        body.Controls.Add(blockHelp);
        _calibrationProgress = CreateBadge("0 / 4 collected", Color.FromArgb(237, 240, 243), Muted);
        _calibrationProgress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _calibrationProgress.Location = new Point(900, 622);
        _calibrationProgress.Size = new Size(160, 28);
        body.Controls.Add(_calibrationProgress);
        body.Resize += (_, _) => _calibrationProgress.Left = Math.Max(
            blockTitle.Right + 12,
            body.ClientSize.Width - _calibrationProgress.Width);

        Panel blockList = new()
        {
            Location = new Point(0, 676),
            Width = 1060,
            Height = 280,
            BackColor = Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(16, 0, 16, 0)
        };
        body.Controls.Add(blockList);
        body.Resize += (_, _) => blockList.Width = body.ClientSize.Width;
        AddBlockRow(blockList, "baseline", "Baseline", "Fixed first", 0);
        AddBlockRow(blockList, "cognitive_heavy", "Cognitive-heavy", "Fixed sequence after baseline", 70);
        AddBlockRow(blockList, "physical_heavy", "Physical-heavy", "Fixed sequence after baseline", 140);
        AddBlockRow(blockList, "temporal_heavy", "Temporal-heavy", "Fixed sequence after baseline", 210);

        Panel gate = new()
        {
            Location = new Point(0, 978),
            Width = 1060,
            Height = 96,
            BackColor = Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(18, 16, 18, 12)
        };
        body.Controls.Add(gate);
        body.Resize += (_, _) => gate.Width = body.ClientSize.Width;
        _profileTitle = CreateLabel("Calibration bundle · unavailable", 11F, Ink, FontStyle.Bold);
        _profileTitle.Location = new Point(18, 16);
        _profileTitle.Size = new Size(520, 24);
        gate.Controls.Add(_profileTitle);
        _profileDetail = CreateLabel(
            "A ready personal knob profile, Baseline plus three demand-probe calibration blocks, and a selected Probe Plugin are required before the calibration bundle can be frozen.",
            9F,
            Muted);
        _profileDetail.Location = new Point(18, 44);
        _profileDetail.Size = new Size(720, 28);
        gate.Controls.Add(_profileDetail);
        _freezeProfileButton = CreatePrimaryButton("Freeze calibration bundle", 202);
        _freezeProfileButton.Enabled = false;
        _freezeProfileButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _freezeProfileButton.Location = new Point(876, 26);
        _freezeProfileButton.Click += FreezeProfile;
        gate.Controls.Add(_freezeProfileButton);
        gate.Resize += (_, _) => _freezeProfileButton.Left = gate.ClientSize.Width - _freezeProfileButton.Width - 18;

        _dataScope = CreateLabel("Active data scope: —", 8.8F, Muted);
        _dataScope.Location = new Point(0, 1090);
        _dataScope.Size = new Size(1020, 24);
        _dataScope.AutoEllipsis = true;
        body.Controls.Add(_dataScope);
        return page;
    }

    private Panel BuildDataPage()
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = Page };
        Label title = CreateLabel("Participant data", 22F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 0);
        title.Size = new Size(430, 34);
        page.Controls.Add(title);
        Label subtitle = CreateLabel(
            "Read-only view of files produced by Unity for the active participant context.",
            9.5F,
            Muted);
        subtitle.Location = new Point(0, 36);
        subtitle.Size = new Size(760, 28);
        page.Controls.Add(subtitle);

        Button openFolder = CreateSecondaryButton("Open participant folder", 190);
        openFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openFolder.Location = new Point(870, 4);
        openFolder.Click += (_, _) =>
        {
            if (_session != null)
                _project.OpenParticipantFolder(_session);
        };
        page.Controls.Add(openFolder);
        page.Resize += (_, _) => openFolder.Left = Math.Max(720, page.ClientSize.Width - openFolder.Width);

        TableLayoutPanel stats = new()
        {
            Location = new Point(0, 78),
            Height = 108,
            Width = 1060,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 3,
            BackColor = Page
        };
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        page.Controls.Add(stats);
        page.Resize += (_, _) => stats.Width = page.ClientSize.Width;
        (Panel filesCard, _dataFileCount, _) = CreateStatCard("Unity data files", "0", "CSV + JSON");
        (Panel runsCard, _dataRunCount, _) = CreateStatCard("Scene runs", "0", "experiment manifests");
        (Panel latestCard, _dataLatest, _) = CreateStatCard("Latest activity", "—", "no participant files yet");
        filesCard.Margin = new Padding(0, 0, 8, 0);
        runsCard.Margin = new Padding(4, 0, 4, 0);
        latestCard.Margin = new Padding(8, 0, 0, 0);
        stats.Controls.Add(filesCard, 0, 0);
        stats.Controls.Add(runsCard, 1, 0);
        stats.Controls.Add(latestCard, 2, 0);

        Label recent = CreateLabel("Latest files", 13F, Ink, FontStyle.Bold);
        recent.Location = new Point(0, 210);
        recent.Size = new Size(220, 28);
        page.Controls.Add(recent);
        Button refresh = CreateGhostButton("Refresh", 92);
        refresh.Location = new Point(968, 204);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Click += async (_, _) => await RefreshDataAsync();
        page.Controls.Add(refresh);
        page.Resize += (_, _) => refresh.Left = Math.Max(820, page.ClientSize.Width - refresh.Width);

        _fileList = new ListView
        {
            Location = new Point(0, 244),
            Size = new Size(1060, 480),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink
        };
        _fileList.Columns.Add("Modified", 150);
        _fileList.Columns.Add("Type", 74);
        _fileList.Columns.Add("Size", 90);
        _fileList.Columns.Add("File", 640);
        _fileList.DoubleClick += (_, _) => OpenSelectedFileLocation();
        page.Controls.Add(_fileList);
        return page;
    }

    private Panel BuildPersonalReferencePage()
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = Page, AutoScroll = true };
        Panel body = new() { Dock = DockStyle.Top, Height = 1130, BackColor = Page };
        page.Controls.Add(body);

        Label title = CreateLabel("Personal knob reference", 22F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 0);
        title.Size = new Size(430, 34);
        body.Controls.Add(title);
        Label subtitle = CreateLabel(
            "Participant-specific movement references produced after the Read calibration. They support review cues and do not label response quality.",
            9.5F,
            Muted);
        subtitle.Location = new Point(0, 36);
        subtitle.Size = new Size(820, 28);
        body.Controls.Add(subtitle);

        Button refresh = CreateGhostButton("Refresh", 92);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Location = new Point(830, 4);
        refresh.Click += async (_, _) => await RefreshDataAsync();
        body.Controls.Add(refresh);

        Button openProfile = CreateSecondaryButton("Show profile file", 150);
        openProfile.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openProfile.Location = new Point(930, 4);
        openProfile.Click += (_, _) => OpenPersonalReferenceProfile();
        body.Controls.Add(openProfile);
        body.Resize += (_, _) =>
        {
            openProfile.Left = body.ClientSize.Width - openProfile.Width;
            refresh.Left = openProfile.Left - refresh.Width - 10;
        };

        TableLayoutPanel stats = new()
        {
            Location = new Point(0, 78),
            Height = 108,
            Width = 1060,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 3,
            BackColor = Page
        };
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        body.Controls.Add(stats);
        body.Resize += (_, _) => stats.Width = body.ClientSize.Width;

        (Panel participantCard, _personalReferenceParticipantValue, _personalReferenceParticipantDetail) =
            CreateStatCard("Profile binding", "No active participant", "Profile must match participant and session");
        (Panel qualityCard, _personalReferenceQualityValue, _personalReferenceQualityDetail) =
            CreateStatCard("Reference status", "Unavailable", "Complete the Read calibration");
        (Panel trialsCard, _personalReferenceTrialsValue, _personalReferenceTrialsDetail) =
            CreateStatCard("Valid reference trials", "0 / 0", "Answer and Confidence target-entry checks");
        participantCard.Margin = new Padding(0, 0, 8, 0);
        qualityCard.Margin = new Padding(4, 0, 4, 0);
        trialsCard.Margin = new Padding(8, 0, 0, 0);
        stats.Controls.Add(participantCard, 0, 0);
        stats.Controls.Add(qualityCard, 1, 0);
        stats.Controls.Add(trialsCard, 2, 0);

        Panel profileCard = CreateCard();
        profileCard.Location = new Point(0, 212);
        profileCard.Size = new Size(1060, 136);
        profileCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        profileCard.Padding = new Padding(20, 16, 20, 14);
        body.Controls.Add(profileCard);
        Label profileHeading = CreateLabel("Active personal reference", 10F, Ink, FontStyle.Bold);
        profileHeading.Location = new Point(20, 16);
        profileHeading.Size = new Size(220, 22);
        profileCard.Controls.Add(profileHeading);
        _personalReferenceStatus = CreateBadge("Awaiting Read calibration", Color.FromArgb(237, 240, 243), Muted);
        _personalReferenceStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _personalReferenceStatus.Location = new Point(830, 14);
        _personalReferenceStatus.Size = new Size(190, 28);
        profileCard.Controls.Add(_personalReferenceStatus);
        profileCard.Resize += (_, _) =>
            _personalReferenceStatus.Left = profileCard.ClientSize.Width - _personalReferenceStatus.Width - 20;

        _personalReferenceSummary = CreateLabel(
            "Complete a valid Read calibration to generate the participant's Answer and Confidence movement reference.",
            9F,
            Muted);
        _personalReferenceSummary.Location = new Point(20, 46);
        _personalReferenceSummary.Size = new Size(1000, 28);
        _personalReferenceSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        profileCard.Controls.Add(_personalReferenceSummary);
        _personalReferencePath = CreateLabel("Profile file: not available", 8.6F, Muted);
        _personalReferencePath.Location = new Point(20, 80);
        _personalReferencePath.Size = new Size(1000, 22);
        _personalReferencePath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _personalReferencePath.AutoEllipsis = true;
        profileCard.Controls.Add(_personalReferencePath);
        Label boundary = CreateLabel(
            "The profile is a descriptive personal reference. It is not a careless/careful label and must remain linked to its raw traces.",
            8.5F,
            Muted);
        boundary.Location = new Point(20, 105);
        boundary.Size = new Size(1000, 20);
        boundary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        profileCard.Controls.Add(boundary);

        Label metricHeading = CreateLabel("Stage-level personal reference", 13F, Ink, FontStyle.Bold);
        metricHeading.Location = new Point(0, 376);
        metricHeading.Size = new Size(360, 28);
        body.Controls.Add(metricHeading);
        Label metricHelp = CreateLabel(
            "These are descriptive Answer and Confidence ranges retained from the target-entry calibration.",
            8.8F,
            Muted);
        metricHelp.Location = new Point(0, 404);
        metricHelp.Size = new Size(880, 22);
        body.Controls.Add(metricHelp);

        _personalReferenceMetricList = CreateReferenceListView(new Point(0, 434), new Size(1060, 276));
        _personalReferenceMetricList.Columns.Add("Stage", 112);
        _personalReferenceMetricList.Columns.Add("Metric", 176);
        _personalReferenceMetricList.Columns.Add("N", 64);
        _personalReferenceMetricList.Columns.Add("Median", 104);
        _personalReferenceMetricList.Columns.Add("P25", 104);
        _personalReferenceMetricList.Columns.Add("P90", 104);
        _personalReferenceMetricList.Columns.Add("Reference range", 236);
        _personalReferenceMetricList.Columns.Add("Units", 128);
        body.Controls.Add(_personalReferenceMetricList);

        Label distanceHeading = CreateLabel("Distance-sensitive X-axis thresholds", 13F, Ink, FontStyle.Bold);
        distanceHeading.Location = new Point(0, 740);
        distanceHeading.Size = new Size(420, 28);
        body.Controls.Add(distanceHeading);
        Label distanceHelp = CreateLabel(
            "High speed uses the personal P90 for the same movement-distance bin. Low correction uses the bin P25.",
            8.8F,
            Muted);
        distanceHelp.Location = new Point(0, 768);
        distanceHelp.Size = new Size(880, 22);
        body.Controls.Add(distanceHelp);

        _personalReferenceDistanceList = CreateReferenceListView(new Point(0, 798), new Size(1060, 188));
        _personalReferenceDistanceList.Columns.Add("Stage", 112);
        _personalReferenceDistanceList.Columns.Add("Movement bin", 184);
        _personalReferenceDistanceList.Columns.Add("Slot distance", 122);
        _personalReferenceDistanceList.Columns.Add("Trials", 74);
        _personalReferenceDistanceList.Columns.Add("Speed P90", 152);
        _personalReferenceDistanceList.Columns.Add("Low correction P25", 182);
        _personalReferenceDistanceList.Columns.Add("Use", 220);
        body.Controls.Add(_personalReferenceDistanceList);

        Label explanationHeading = CreateLabel("How the current response-process cue uses this profile", 13F, Ink, FontStyle.Bold);
        explanationHeading.Location = new Point(0, 1018);
        explanationHeading.Size = new Size(540, 28);
        body.Controls.Add(explanationHeading);
        _personalReferenceExplanation = new TextBox
        {
            Location = new Point(0, 1050),
            Size = new Size(1060, 62),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9F),
            ScrollBars = ScrollBars.Vertical,
            Text = "Complete a valid Read calibration to view participant-specific response-process thresholds."
        };
        body.Controls.Add(_personalReferenceExplanation);
        return page;
    }

    private Panel BuildProbeDefinitionPage()
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = Page };
        Label title = CreateLabel("Probe plugins", 22F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 0);
        title.Size = new Size(430, 34);
        page.Controls.Add(title);
        Label subtitle = CreateLabel(
            "Build a reusable task-probe plugin from calibration exports, then select it as the Evidence Matrix Y-axis definition.",
            9.5F,
            Muted);
        subtitle.Location = new Point(0, 36);
        subtitle.Size = new Size(860, 28);
        page.Controls.Add(subtitle);

        Button build = CreatePrimaryButton("Build / edit plugin", 178);
        build.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        build.Location = new Point(730, 4);
        build.Click += (_, _) => OpenProbeBuilder();
        page.Controls.Add(build);
        Button openFolder = CreateSecondaryButton("Open plugin folder", 170);
        openFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openFolder.Location = new Point(910, 4);
        openFolder.Click += (_, _) =>
        {
            if (_session == null)
                return;
            string folder = ProbeRuleCardStore.PluginDirectory(_session.OutputRoot);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        };
        page.Controls.Add(openFolder);
        page.Resize += (_, _) =>
        {
            openFolder.Left = Math.Max(820, page.ClientSize.Width - openFolder.Width);
            build.Left = openFolder.Left - build.Width - 10;
        };

        Panel activeCard = CreateCard();
        activeCard.Location = new Point(0, 82);
        activeCard.Size = new Size(1060, 112);
        activeCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        activeCard.Padding = new Padding(20, 16, 20, 14);
        page.Controls.Add(activeCard);
        Label activeHeading = CreateLabel("Selected plugin", 10F, Ink, FontStyle.Bold);
        activeHeading.Location = new Point(20, 16);
        activeHeading.Size = new Size(150, 24);
        activeCard.Controls.Add(activeHeading);
        _probePluginValue = CreateLabel("No selected plugin", 13F, Ink, FontStyle.Bold);
        _probePluginValue.Location = new Point(20, 42);
        _probePluginValue.Size = new Size(630, 28);
        _probePluginValue.AutoEllipsis = true;
        activeCard.Controls.Add(_probePluginValue);
        _probePluginDetail = CreateLabel("Build a Probe Plugin from multiple calibration runs before using the Evidence Matrix Y axis.", 8.8F, Muted);
        _probePluginDetail.Location = new Point(20, 73);
        _probePluginDetail.Size = new Size(800, 23);
        _probePluginDetail.AutoEllipsis = true;
        activeCard.Controls.Add(_probePluginDetail);
        Button useSelected = CreatePrimaryButton("Use selected", 138);
        useSelected.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        useSelected.Location = new Point(900, 35);
        useSelected.Click += (_, _) => SetSelectedPluginActive();
        activeCard.Controls.Add(useSelected);
        activeCard.Resize += (_, _) => useSelected.Left = activeCard.ClientSize.Width - useSelected.Width - 20;

        Label libraryHeading = CreateLabel("Plugin library", 13F, Ink, FontStyle.Bold);
        libraryHeading.Location = new Point(0, 220);
        libraryHeading.Size = new Size(220, 28);
        page.Controls.Add(libraryHeading);
        Label libraryHelp = CreateLabel(
            "A plugin keeps its source calibration metadata, selected feature directions, and task scope. Selecting it does not modify Unity data or task logic.",
            8.8F,
            Muted);
        libraryHelp.Location = new Point(0, 248);
        libraryHelp.Size = new Size(950, 24);
        page.Controls.Add(libraryHelp);

        _probePluginList = new ListView
        {
            Location = new Point(0, 282),
            Size = new Size(1060, 410),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink
        };
        _probePluginList.Columns.Add("Active", 76);
        _probePluginList.Columns.Add("Plugin", 235);
        _probePluginList.Columns.Add("Calibration", 135);
        _probePluginList.Columns.Add("Dimensions", 230);
        _probePluginList.Columns.Add("Scope", 190);
        _probePluginList.Columns.Add("File", 500);
        _probePluginList.DoubleClick += (_, _) => SetSelectedPluginActive();
        page.Controls.Add(_probePluginList);
        return page;
    }

    private void OpenProbeBuilder()
    {
        if (_session == null || _snapshot == null)
        {
            SetStatus("Start a participant session and complete a Probe calibration run before building a Plugin.", Warning);
            return;
        }
        using var dialog = new ProbeDefinitionDialog(_project, _session, _snapshot);
        dialog.ShowDialog(this);
        RefreshProbePluginSummary();
        UpdateEvidenceMatrixStatus();
    }

    private void SetSelectedPluginActive()
    {
        if (_session == null || _probePluginList.SelectedItems.Count == 0 ||
            _probePluginList.SelectedItems[0].Tag is not ProbePluginFile plugin ||
            string.IsNullOrWhiteSpace(plugin.FilePath))
            return;
        ProbeRuleCardStore.SetActive(_session.OutputRoot, plugin.FilePath);
        RefreshProbePluginSummary();
        UpdateEvidenceMatrixStatus();
        SetStatus($"Selected Probe Plugin: {_activeProbePlugin.PluginName}", Success);
    }

    private void RefreshProbePluginSummary()
    {
        if (_session == null)
            return;
        ProbePluginFile active = ProbeRuleCardStore.LoadActive(_session.OutputRoot);
        _activeProbePlugin = active.Plugin;
        _activeProbePluginPath = active.FilePath;
        if (_probePluginValue != null)
        {
            bool hasPlugin = !string.IsNullOrWhiteSpace(active.FilePath) && _activeProbePlugin.Dimensions.Count > 0;
            _probePluginValue.Text = hasPlugin ? _activeProbePlugin.PluginName : "No selected plugin";
            _probePluginDetail.Text = hasPlugin
                ? $"{_activeProbePlugin.Dimensions.Count} calibrated dimension(s) · calibration n={_activeProbePlugin.CalibrationParticipantCount} · {_activeProbePlugin.BoundaryNote}"
                : "Build a Probe Plugin from multiple calibration runs before using the Evidence Matrix Y axis.";
        }
        if (_probePluginList == null)
            return;
        _probePluginList.BeginUpdate();
        try
        {
            _probePluginList.Items.Clear();
            foreach (ProbePluginFile plugin in ProbeRuleCardStore.ListPlugins(_session.OutputRoot))
            {
                var item = new ListViewItem(plugin.IsActive ? "Selected" : "");
                item.SubItems.Add(plugin.Plugin.PluginName);
                item.SubItems.Add($"n={plugin.Plugin.CalibrationParticipantCount}");
                item.SubItems.Add(string.Join(", ", plugin.Plugin.Dimensions.Select(card => card.DisplayName)));
                item.SubItems.Add(string.Join(", ", plugin.Plugin.Dimensions.Select(card => card.Scope.Replace('_', ' ')).Distinct()));
                item.SubItems.Add(plugin.FilePath);
                item.Tag = plugin;
                _probePluginList.Items.Add(item);
            }
        }
        finally
        {
            _probePluginList.EndUpdate();
        }
    }

    private Panel BuildReviewPage()
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = Page, AutoScroll = true };
        Label title = CreateLabel("Evidence review", 22F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 0);
        title.Size = new Size(430, 34);
        page.Controls.Add(title);
        Label subtitle = CreateLabel(
            "Contextual records keep personal response-process evidence and task-probe evidence separate for researcher review.",
            9.5F,
            Muted);
        subtitle.Location = new Point(0, 36);
        subtitle.Size = new Size(980, 28);
        page.Controls.Add(subtitle);

        Label filterLabel = CreateLabel("Target dimension", 9F, Ink, FontStyle.Bold);
        filterLabel.Location = new Point(0, 78);
        filterLabel.Size = new Size(140, 26);
        page.Controls.Add(filterLabel);
        _matrixDimensionFilter = new ComboBox
        {
            Location = new Point(144, 75),
            Size = new Size(220, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(Font.FontFamily, 9.5F),
            BackColor = Surface,
            ForeColor = Ink
        };
        _matrixDimensionFilter.Items.AddRange(new object[]
        {
            "Mental Demand", "Physical Demand"
        });
        _matrixDimensionFilter.SelectedIndex = 0;
        _matrixDimensionFilter.SelectedIndexChanged += (_, _) => UpdateEvidenceMatrixStatus();
        page.Controls.Add(_matrixDimensionFilter);

        _matrixStatus = CreateLabel("Awaiting a personal response profile and task-probe calibration.", 9F, Muted);
        _matrixStatus.Location = new Point(386, 78);
        _matrixStatus.Size = new Size(440, 26);
        _matrixStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        page.Controls.Add(_matrixStatus);
        _buildEvidenceButton = CreatePrimaryButton("Build contextual record", 194);
        _buildEvidenceButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _buildEvidenceButton.Location = new Point(866, 72);
        _buildEvidenceButton.Click += (_, _) => BuildContextualRecord();
        page.Controls.Add(_buildEvidenceButton);
        page.Resize += (_, _) => _buildEvidenceButton.Left = Math.Max(830, page.ClientSize.Width - _buildEvidenceButton.Width);

        Panel card = CreateCard();
        card.Location = new Point(0, 122);
        card.Size = new Size(1060, 650);
        card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Padding = new Padding(22);
        page.Controls.Add(card);

        Label heading = CreateLabel("Contextual Evidence Matrix", 14F, Ink, FontStyle.Bold);
        heading.Location = new Point(22, 18);
        heading.Size = new Size(460, 28);
        card.Controls.Add(heading);
        Label matrixBoundary = CreateLabel(
            "X is a participant-relative knob pattern. Y is a selected Probe Plugin's task-context match. The matrix is not a combined data-quality score.",
            8.8F,
            Muted);
        matrixBoundary.Location = new Point(22, 48);
        matrixBoundary.Size = new Size(920, 24);
        card.Controls.Add(matrixBoundary);

        TableLayoutPanel matrix = new()
        {
            Location = new Point(22, 82),
            Size = new Size(1016, 250),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 4,
            RowCount = 4,
            BackColor = Line,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        matrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23F));
        matrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25.67F));
        matrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25.67F));
        matrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25.66F));
        matrix.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        matrix.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        matrix.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        matrix.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
        card.Controls.Add(matrix);
        card.Resize += (_, _) => matrix.Width = Math.Max(760, card.ClientSize.Width - 44);

        AddMatrixHeader(matrix, "Y: Selected Probe Plugin", 0, 0);
        AddMatrixHeader(matrix, "X: Matches rapid /\ndirect pattern", 1, 0);
        AddMatrixHeader(matrix, "X: No dominant\nresponse pattern", 2, 0);
        AddMatrixHeader(matrix, "X: Matches hesitant /\ncorrective pattern", 3, 0);
        AddMatrixRowHeader(matrix, "Y: Strong\nProbe-pattern match", 0, 1);
        AddMatrixRowHeader(matrix, "Y: Partial\nProbe-pattern match", 0, 2);
        AddMatrixRowHeader(matrix, "Y: No match /\ninsufficient evidence", 0, 3);
        for (int row = 1; row <= 3; row++)
        {
            for (int column = 1; column <= 3; column++)
                AddMatrixPlaceholder(matrix, column, row);
        }

        Label footer = CreateLabel(
            "A point groups an item by independently calculated X and Y evidence. Rating and confidence remain properties of the item; neither is used to calculate the matrix position.",
            8.8F,
            Muted);
        footer.Location = new Point(22, 350);
        footer.Size = new Size(970, 56);
        footer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(footer);
        Label detailHeading = CreateLabel("Selected item evidence", 10F, Ink, FontStyle.Bold);
        detailHeading.Location = new Point(22, 416);
        detailHeading.Size = new Size(320, 24);
        card.Controls.Add(detailHeading);
        _matrixDetail = new TextBox
        {
            Location = new Point(22, 444),
            Size = new Size(1016, 178),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 252, 253),
            ForeColor = Ink,
            Font = new Font(Font.FontFamily, 8.8F),
            Text = "Build a contextual record, then select a populated matrix cell to inspect its linked raw evidence."
        };
        card.Controls.Add(_matrixDetail);
        return page;
    }

    private void AddMatrixHeader(TableLayoutPanel matrix, string text, int column, int row)
    {
        Label label = CreateLabel(text, 8.7F, Ink, FontStyle.Bold);
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(1);
        label.Padding = new Padding(7, 4, 7, 4);
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.BackColor = Color.FromArgb(236, 242, 246);
        matrix.Controls.Add(label, column, row);
    }

    private void AddMatrixRowHeader(TableLayoutPanel matrix, string text, int column, int row)
    {
        Label label = CreateLabel(text, 8.7F, Ink, FontStyle.Bold);
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(1);
        label.Padding = new Padding(7, 4, 7, 4);
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.BackColor = Color.FromArgb(244, 247, 249);
        matrix.Controls.Add(label, column, row);
    }

    private void AddMatrixPlaceholder(TableLayoutPanel matrix, int column, int row)
    {
        Label label = CreateLabel("Awaiting calibrated\ntarget-study response", 8.4F, Muted);
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(1);
        label.Padding = new Padding(6);
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.BackColor = Surface;
        label.Cursor = Cursors.Hand;
        label.Click += (_, _) => ShowSelectedMatrixCell(row, column);
        matrix.Controls.Add(label, column, row);
        _matrixCells[(row, column)] = label;
    }

    private void UpdateEvidenceMatrixStatus()
    {
        if (_matrixStatus == null || _matrixDimensionFilter == null)
            return;
        string dimension = _matrixDimensionFilter.SelectedItem?.ToString() ?? "selected dimension";
        if (_snapshot == null)
        {
            _matrixStatus.Text = "Awaiting a participant context, personal response profile, and task-probe calibration.";
            if (_buildEvidenceButton != null)
                _buildEvidenceButton.Enabled = false;
            ResetMatrixCells("Awaiting calibrated\ntarget-study response");
            return;
        }

        bool personalReady = _snapshot.ResponseCalibration.Ready;
        bool probeReady = _snapshot.CalibrationComplete;
        ProbeDimensionDefinition? definition = ProbeDimensionCatalog.FromItemDimension(dimension);
        bool pluginReady = definition != null && _activeProbePlugin.Find(definition.Id) is { Features.Count: > 0 };
        if (_buildEvidenceButton != null)
            _buildEvidenceButton.Enabled = personalReady && probeReady && pluginReady;
        if (_contextualRecords.Count > 0)
        {
            _matrixStatus.Text = $"{dimension}: {_contextualRecords.Count} contextual records are loaded. Select a matrix cell to inspect its evidence.";
            PopulateEvidenceMatrix();
            return;
        }
        if (!pluginReady)
        {
            _matrixStatus.Text = $"{dimension}: select a Probe Plugin containing this calibrated dimension before projecting Y-axis evidence.";
            ResetMatrixCells("Awaiting selected\nProbe Plugin");
            return;
        }
        _matrixStatus.Text = personalReady && probeReady
            ? $"{dimension}: calibration bundle ready. No target-study items have been projected into the matrix yet."
            : $"{dimension}: personal profile {(personalReady ? "ready" : "pending")} · Probe calibration {(probeReady ? "ready" : "pending")} · no projection is shown.";
        string placeholder = personalReady && probeReady
            ? "No target-study\nresponses yet"
            : "Awaiting calibrated\ntarget-study response";
        foreach (Label cell in _matrixCells.Values)
            cell.Text = placeholder;
    }

    private void BuildContextualRecord()
    {
        if (_session == null || _snapshot == null)
            return;
        RefreshProbePluginSummary();
        var service = new ContextualEvidenceService();
        ContextualEvidenceResult result = service.Build(_session, _snapshot, _activeProbePlugin);
        if (!result.Success)
        {
            SetStatus(result.Message, Warning);
            _matrixStatus.Text = result.Message;
            return;
        }

        _contextualRecords = result.Records;
        _matrixDetail.Text = $"Derived record written to:\r\n{result.OutputPath}\r\n\r\n{result.Message}\r\n\r\nSelect a populated matrix cell to inspect item-level X and Y evidence.";
        SetStatus(result.Message, Success);
        UpdateEvidenceMatrixStatus();
    }

    private void PopulateEvidenceMatrix()
    {
        ResetMatrixCells("No projected item");
        if (_matrixDimensionFilter.SelectedItem is not string selectedDimension)
            return;
        ProbeDimensionDefinition? definition = ProbeDimensionCatalog.FromItemDimension(selectedDimension);
        if (definition == null)
            return;

        IEnumerable<ContextualResponseRecord> filtered = _contextualRecords.Where(record =>
            ProbeDimensionCatalog.FromItemDimension(record.ItemDimension)?.Id == definition.Id);
        foreach (IGrouping<(int Row, int Column), ContextualResponseRecord> group in filtered.GroupBy(record =>
                     (MatrixRow(record.YPattern), MatrixColumn(record.XPattern))))
        {
            if (!_matrixCells.TryGetValue(group.Key, out Label? cell))
                continue;
            List<ContextualResponseRecord> records = group.OrderBy(record => record.PresentationOrder)
                .ThenBy(record => record.ItemId, StringComparer.OrdinalIgnoreCase).ToList();
            cell.Tag = records;
            cell.BackColor = group.Key.Row switch
            {
                1 => Color.FromArgb(231, 244, 236),
                2 => Color.FromArgb(250, 244, 226),
                _ => Color.FromArgb(245, 238, 238)
            };
            cell.Text = string.Join("\n", records.Take(3).Select(record =>
                $"{record.BlockId}: {record.ItemId}\nscore {record.SelectedScore}; conf {record.Confidence}"));
            if (records.Count > 3)
                cell.Text += $"\n+ {records.Count - 3} more";
        }
    }

    private void ResetMatrixCells(string text)
    {
        foreach (Label cell in _matrixCells.Values)
        {
            cell.Text = text;
            cell.Tag = null;
            cell.BackColor = Surface;
        }
    }

    private void ShowSelectedMatrixCell(int row, int column)
    {
        if (_matrixDetail == null || !_matrixCells.TryGetValue((row, column), out Label? cell) ||
            cell.Tag is not IReadOnlyList<ContextualResponseRecord> records || records.Count == 0)
            return;
        var builder = new StringBuilder();
        foreach (ContextualResponseRecord record in records)
        {
            builder.AppendLine($"Item: {record.ItemId} ({record.ItemDimension}) / block: {record.BlockId}");
            builder.AppendLine($"Rating: {record.SelectedScore}; Confidence: {record.Confidence}; Baseline rating: {(record.BaselineScore?.ToString("0.###") ?? "not available")}");
            builder.AppendLine($"X / Answer pattern: {record.XPattern}");
            builder.AppendLine($"X evidence: {record.XEvidence}");
            builder.AppendLine($"Confidence-stage pattern: {record.ConfidencePattern}");
            builder.AppendLine($"Y / Probe pattern match: {record.YPattern} ({record.ProbeMatchRatio:P0})");
            builder.AppendLine($"Y evidence: {record.YEvidence}");
            builder.AppendLine($"Score context: {record.ScoreContext}");
            builder.AppendLine($"Sources: questionnaire={record.SourceQuestionnairePath}");
            builder.AppendLine($"         combined metrics={record.SourceCombinedMetricsPath}");
            builder.AppendLine($"         baseline metrics={record.SourceBaselineMetricsPath}");
            builder.AppendLine($"         response profile={record.SourceResponseProfilePath}");
            builder.AppendLine();
        }
        _matrixDetail.Text = builder.ToString();
    }

    private static int MatrixColumn(string pattern) => pattern switch
    {
        "accelerated_direct" => 1,
        "hesitant_corrective" => 3,
        _ => 2
    };

    private static int MatrixRow(string pattern) => pattern switch
    {
        "strong" => 1,
        "partial" => 2,
        _ => 3
    };

    private void AddSceneRow(Panel parent, SceneDefinition scene, int top, bool primary)
    {
        Panel row = new()
        {
            Left = 16,
            Top = top + 2,
            Height = 78,
            Width = parent.ClientSize.Width - 32,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Surface
        };
        if (top > 0)
        {
            Panel line = new() { Dock = DockStyle.Top, Height = 1, BackColor = Line };
            row.Controls.Add(line);
        }
        parent.Controls.Add(row);

        Label title = CreateLabel(scene.DisplayName, 10.5F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 13);
        title.Size = new Size(430, 24);
        row.Controls.Add(title);
        Label description = CreateLabel($"{scene.SceneName} · {scene.Description}", 8.8F, Muted);
        description.Location = new Point(0, 39);
        description.Size = new Size(720, 24);
        description.AutoEllipsis = true;
        row.Controls.Add(description);

        Button launch = primary
            ? CreatePrimaryButton("Launch calibration scene", 202)
            : CreateSecondaryButton(scene == SceneDefinitions.Combined ? "Requires calibration" : "Launch scene", 170);
        launch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        launch.Location = new Point(row.ClientSize.Width - launch.Width, 21);
        launch.Enabled = scene != SceneDefinitions.Combined;
        launch.Click += (_, _) => LaunchScene(scene);
        row.Controls.Add(launch);
        row.Resize += (_, _) => launch.Left = row.ClientSize.Width - launch.Width;
        if (scene == SceneDefinitions.Combined)
            _combinedLaunchButton = launch;
    }

    private void AddBlockRow(Panel parent, string id, string name, string orderText, int top)
    {
        Panel row = new()
        {
            Left = 16,
            Top = top,
            Width = parent.ClientSize.Width - 32,
            Height = 70,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Surface
        };
        if (top > 0)
            row.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Line });
        parent.Controls.Add(row);

        Label order = CreateBadge(id == "baseline" ? "1" : "—", Color.FromArgb(237, 240, 243), Muted);
        order.Location = new Point(0, 19);
        order.Size = new Size(34, 30);
        order.TextAlign = ContentAlignment.MiddleCenter;
        row.Controls.Add(order);
        Label title = CreateLabel(name, 10.2F, Ink, FontStyle.Bold);
        title.Location = new Point(48, 10);
        title.Size = new Size(260, 24);
        row.Controls.Add(title);
        Label meta = CreateLabel($"{orderText} · questionnaire + probe + behavior streams", 8.7F, Muted);
        meta.Location = new Point(48, 36);
        meta.Size = new Size(620, 22);
        row.Controls.Add(meta);
        Label status = CreateBadge("Queued", Color.FromArgb(237, 240, 243), Muted);
        status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        status.Location = new Point(row.ClientSize.Width - 128, 20);
        status.Size = new Size(118, 28);
        status.TextAlign = ContentAlignment.MiddleCenter;
        row.Controls.Add(status);
        row.Resize += (_, _) => status.Left = row.ClientSize.Width - status.Width - 10;
        _blockViews[id] = new BlockRowView(order, status, meta);
    }

    private async void EnterSession(object? sender, EventArgs eventArgs)
    {
        if (!_project.TryCreateSession(
                _participantInput.Text,
                Decimal.ToInt32(_sessionInput.Value),
                _outputInput.Text,
                out ResearchSession? session,
                out string error))
        {
            _entryError.Text = error;
            return;
        }

        _entryError.Text = "";
        _session = session;
        _headerContext.Text = $"{session!.ParticipantId}  ·  Session {session.SessionNumber}";
        _participantValue.Text = session.ParticipantId;
        _dataScope.Text = $"Active data scope: {Path.Combine(session.OutputRoot, session.ParticipantId)}";
        _consoleView.Visible = true;
        _consoleView.BringToFront();
        _entryView.Visible = false;
        SetStatus($"Participant context locked: {session.ParticipantId} · Session {session.SessionNumber}.", Success);
        _refreshTimer.Start();
        await RefreshDataAsync();
    }

    private void EndSession()
    {
        _refreshTimer.Stop();
        _session = null;
        _snapshot = null;
        _participantInput.Clear();
        _sessionInput.Value = 1;
        _entryError.Text = "";
        ResetConsoleState();
        ShowEntryView();
        _participantInput.Focus();
    }

    private void LaunchScene(SceneDefinition scene)
    {
        if (_session == null)
            return;
        if (scene == SceneDefinitions.Combined && !WorkflowCalibrationReady())
        {
            SetStatus("Combined remains locked until the personal knob profile, Baseline plus three demand-probe blocks, and a selected Probe Plugin are ready.", Warning);
            return;
        }

        try
        {
            string message = _project.QueueSceneLaunch(_session, scene);
            _currentSceneValue.Text = scene.SceneName;
            _currentSceneDetail.Text = "Launch requested · Unity owns data collection";
            _pipelineBadge.Text = "Launch queued";
            SetStatus(message, Accent);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not queue scene launch: {exception.Message}", Error);
        }
    }

    private void FreezeProfile(object? sender, EventArgs eventArgs)
    {
        if (_session == null || _snapshot == null || !WorkflowCalibrationReady())
            return;

        try
        {
            string directory = _project.ConsoleSessionDirectory(_session);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "carexr_calibration_bundle.json");
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = "CAREXR_CalibrationBundle_v1",
                participantId = _session.ParticipantId,
                sessionNumber = _session.SessionNumber,
                frozenAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                source = "Unity personal PAXSM response calibration and Workload Probe calibration exports",
                rawDataModified = false,
                 responseCalibration = new
                {
                    profilePath = _snapshot.ResponseCalibration.ProfilePath,
                    quality = _snapshot.ResponseCalibration.Quality,
                    personalTrials = _snapshot.ResponseCalibration.PersonalTrials,
                    personalReferenceTrials = _snapshot.ResponseCalibration.PersonalReferenceTrials,
                    personalAnswerAccuracy = _snapshot.ResponseCalibration.PersonalAnswerAccuracy,
                    personalConfidenceAccuracy = _snapshot.ResponseCalibration.PersonalConfidenceAccuracy
                 },
                 probePlugin = new
                 {
                     pluginName = _activeProbePlugin.PluginName,
                     pluginPath = _activeProbePluginPath,
                     calibrationParticipantCount = _activeProbePlugin.CalibrationParticipantCount,
                     dimensions = _activeProbePlugin.Dimensions.Select(card => new
                     {
                         card.DimensionId,
                         card.DisplayName,
                         card.CalibrationBlockId,
                         card.Scope,
                         featureIds = card.Features.Select(feature => feature.MetricId).ToArray()
                     })
                 },
                blocks = _snapshot.CalibrationBlocks.Select(block => new
                {
                    blockId = block.BlockId,
                    presentationOrder = block.PresentationOrder,
                    questionnaireItemCount = block.QuestionnaireItemCount
                })
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            _profileTitle.Text = "Calibration bundle · frozen";
            _profileDetail.Text = "Freeze record saved separately; Unity raw data was not modified.";
            _freezeProfileButton.Enabled = false;
            _combinedLaunchButton.Enabled = true;
            _combinedLaunchButton.Text = "Launch scene";
            _calibrationDetail.Text = "Bundle frozen";
            UpdateEvidenceMatrixStatus();
            SetStatus($"Calibration bundle saved: {path}", Success);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save calibration freeze record: {exception.Message}", Error);
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_session == null || _refreshing)
            return;
        _refreshing = true;
        try
        {
            ResearchSession session = _session;
            DataSnapshot snapshot = await Task.Run(() => _dataScanner.Scan(session));
            if (_session != session || IsDisposed)
                return;
            _snapshot = snapshot;
            ApplySnapshot(snapshot);
        }
        catch (Exception exception)
        {
            SetStatus($"Data refresh failed: {exception.Message}", Error);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplySnapshot(DataSnapshot snapshot)
    {
        RefreshProbePluginSummary();
        int collected = snapshot.CalibrationBlocks.Count(block =>
            block.State == CalibrationBlockState.Collected);
        int expectedProbeBlocks = snapshot.CalibrationBlocks.Count;
        _calibrationValue.Text = $"{collected} / {expectedProbeBlocks}";
        _calibrationProgress.Text = $"{collected} / {expectedProbeBlocks} collected";
        _calibrationDetail.Text = snapshot.CalibrationComplete
            ? "Probe blocks ready"
            : snapshot.Runs.Any(run => run.SceneName == SceneDefinitions.Workload.SceneName)
                ? "Reading Unity checkpoints"
                : "Not started";

        foreach (CalibrationBlockSnapshot block in snapshot.CalibrationBlocks)
        {
            if (!_blockViews.TryGetValue(block.BlockId, out BlockRowView? view))
                continue;
            view.Order.Text = block.PresentationOrder?.ToString() ?? (block.BlockId == "baseline" ? "1" : "—");
            switch (block.State)
            {
                case CalibrationBlockState.Collected:
                    view.Status.Text = "Collected";
                    view.Status.BackColor = Color.FromArgb(226, 242, 233);
                    view.Status.ForeColor = Success;
                    break;
                case CalibrationBlockState.QuestionnairePartial:
                    view.Status.Text = $"Questionnaire {block.QuestionnaireItemCount}/6";
                    view.Status.BackColor = Color.FromArgb(249, 239, 219);
                    view.Status.ForeColor = Warning;
                    break;
                default:
                    view.Status.Text = "Queued";
                    view.Status.BackColor = Color.FromArgb(237, 240, 243);
                    view.Status.ForeColor = Muted;
                    break;
            }
        }

        ResponseCalibrationProfileSnapshot responseProfile = snapshot.ResponseCalibration;
        string personalProfileState = !responseProfile.Found
            ? "Personal knob profile has not been exported."
            : responseProfile.Ready
                ? $"Personal knob profile ready: {responseProfile.PersonalReferenceTrials} valid personal-reference trials."
                : $"Personal knob profile is {responseProfile.Quality}: {responseProfile.PersonalTrials} formal trials, {responseProfile.PersonalReferenceTrials} valid reference trials.";

        if (WorkflowCalibrationReady())
        {
            _profileTitle.Text = "Calibration bundle · ready to freeze";
            _profileDetail.Text = personalProfileState + " Baseline plus three demand-probe blocks and a selected Probe Plugin are ready.";
            _freezeProfileButton.Enabled = true;
        }
        else
        {
            _profileTitle.Text = "Calibration bundle · incomplete";
            _profileDetail.Text = personalProfileState + $" Task Probe blocks collected: {collected}/{expectedProbeBlocks}. Select a Probe Plugin to complete the workflow.";
            _freezeProfileButton.Enabled = false;
        }

        _combinedLaunchButton.Enabled = WorkflowCalibrationReady();
        _combinedLaunchButton.Text = WorkflowCalibrationReady() ? "Launch scene" : "Requires calibration";
        _dataFileCount.Text = (snapshot.CsvFileCount + snapshot.JsonFileCount).ToString();
        _dataRunCount.Text = snapshot.Runs.Count.ToString();
        _dataLatest.Text = snapshot.LatestWriteUtc.HasValue
            ? snapshot.LatestWriteUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "—";
        PopulateFileList(snapshot.LatestFiles);
        PopulatePersonalReferencePage(snapshot);
        UpdateEvidenceMatrixStatus();
    }

    private void PopulatePersonalReferencePage(DataSnapshot snapshot)
    {
        if (_personalReferenceMetricList == null || _personalReferenceDistanceList == null)
            return;

        PersonalReferenceProfileDetails profile = _personalReferenceReader.Read(snapshot.ResponseCalibration, _session);
        _personalReferenceMetricList.BeginUpdate();
        _personalReferenceDistanceList.BeginUpdate();
        try
        {
            _personalReferenceMetricList.Items.Clear();
            _personalReferenceDistanceList.Items.Clear();

            string activeContext = _session == null
                ? "No active participant"
                : $"{_session.ParticipantId} / Session {_session.SessionNumber}";
            _personalReferenceParticipantValue.Text = activeContext;
            _personalReferenceParticipantDetail.Text = profile.BoundToActiveSession
                ? "Profile participant and session verified"
                : "Profile must match the active participant and session";

            if (!profile.Found || !profile.BoundToActiveSession)
            {
                _personalReferenceQualityValue.Text = "Unavailable";
                _personalReferenceQualityDetail.Text = profile.Message;
                _personalReferenceTrialsValue.Text = "0 / 0";
                _personalReferenceTrialsDetail.Text = "No valid target-entry reference is linked";
                SetPersonalReferenceStatus("Awaiting Read calibration", Color.FromArgb(237, 240, 243), Muted);
                _personalReferenceSummary.Text = profile.Message;
                _personalReferencePath.Text = string.IsNullOrWhiteSpace(profile.ProfilePath)
                    ? "Profile file: not available"
                    : $"Profile file: {profile.ProfilePath}";
                _personalReferenceExplanation.Text =
                    "The X-axis is intentionally unavailable until a completed personal knob reference is linked to the active participant and session.";
                return;
            }

            _personalReferenceQualityValue.Text = profile.ProfileReady ? "Ready" : ToDisplayText(profile.ProfileQuality);
            _personalReferenceQualityDetail.Text = profile.ProfileReady
                ? "Personal reference can support response-process review cues"
                : "Profile is retained for audit but is not eligible for X-axis matching";
            _personalReferenceTrialsValue.Text = $"{profile.ReferenceTrials} / {profile.MinimumReferenceTrials}";
            _personalReferenceTrialsDetail.Text =
                $"Answer {FormatPercent(profile.AnswerTargetAccuracy)} | Confidence {FormatPercent(profile.ConfidenceTargetAccuracy)}";
            SetPersonalReferenceStatus(
                profile.ProfileReady ? "Profile ready" : $"Profile {ToDisplayText(profile.ProfileQuality)}",
                profile.ProfileReady ? Color.FromArgb(226, 242, 233) : Color.FromArgb(249, 239, 219),
                profile.ProfileReady ? Success : Warning);
            _personalReferenceSummary.Text =
                $"{profile.Message} Source: {profile.SourceScene} | Run: {profile.RunId} | Generated: {profile.GeneratedUtc}";
            _personalReferencePath.Text = $"Profile file: {profile.ProfilePath}";

            foreach (PersonalReferenceStage stage in new[] { profile.Answer, profile.Confidence })
            {
                foreach (string metricId in PersonalReferenceProfileReader.CoreMetrics)
                {
                    if (!stage.Metrics.TryGetValue(metricId, out PersonalReferenceMetric? metric))
                        continue;
                    var item = new ListViewItem(stage.Name);
                    item.SubItems.Add(ToMetricDisplayName(metricId));
                    item.SubItems.Add(metric.SampleCount.ToString());
                    item.SubItems.Add(FormatReferenceValue(metric.Median));
                    item.SubItems.Add(FormatReferenceValue(metric.P25));
                    item.SubItems.Add(FormatReferenceValue(metric.P90));
                    item.SubItems.Add($"{FormatReferenceValue(metric.LowerReference)} to {FormatReferenceValue(metric.UpperReference)}");
                    item.SubItems.Add(string.IsNullOrWhiteSpace(metric.Units) ? "--" : metric.Units.Replace('_', ' '));
                    _personalReferenceMetricList.Items.Add(item);
                }

                foreach (PersonalReferenceDistanceBin bin in stage.DistanceBins)
                {
                    var item = new ListViewItem(stage.Name);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(bin.DisplayName) ? bin.BinId : bin.DisplayName);
                    item.SubItems.Add(FormatSlotDistance(bin.MinimumSlotDistance, bin.MaximumSlotDistance));
                    item.SubItems.Add(bin.ReferenceTrialCount.ToString());
                    item.SubItems.Add(FormatReferenceValue(bin.MaxAbsVelocity.P90));
                    item.SubItems.Add(FormatReferenceValue(bin.CorrectionRate.P25));
                    item.SubItems.Add("High speed and low-correction comparison");
                    _personalReferenceDistanceList.Items.Add(item);
                }
            }

            _personalReferenceExplanation.Text = BuildPersonalReferenceExplanation(profile);
        }
        finally
        {
            _personalReferenceMetricList.EndUpdate();
            _personalReferenceDistanceList.EndUpdate();
        }
    }

    private void ResetPersonalReferencePage()
    {
        if (_personalReferenceMetricList == null || _personalReferenceDistanceList == null)
            return;

        _personalReferenceParticipantValue.Text = "No active participant";
        _personalReferenceParticipantDetail.Text = "Profile must match participant and session";
        _personalReferenceQualityValue.Text = "Unavailable";
        _personalReferenceQualityDetail.Text = "Complete the Read calibration";
        _personalReferenceTrialsValue.Text = "0 / 0";
        _personalReferenceTrialsDetail.Text = "Answer and Confidence target-entry checks";
        SetPersonalReferenceStatus("Awaiting Read calibration", Color.FromArgb(237, 240, 243), Muted);
        _personalReferenceSummary.Text = "Complete a valid Read calibration to generate the participant's Answer and Confidence movement reference.";
        _personalReferencePath.Text = "Profile file: not available";
        _personalReferenceExplanation.Text = "Complete a valid Read calibration to view participant-specific response-process thresholds.";
        _personalReferenceMetricList.Items.Clear();
        _personalReferenceDistanceList.Items.Clear();
    }

    private void OpenPersonalReferenceProfile()
    {
        string path = _snapshot?.ResponseCalibration.ProfilePath ?? "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetStatus("No personal knob reference profile is available for the active participant/session.", Warning);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    private void SetPersonalReferenceStatus(string text, Color background, Color foreground)
    {
        _personalReferenceStatus.Text = text;
        _personalReferenceStatus.BackColor = background;
        _personalReferenceStatus.ForeColor = foreground;
    }

    private static string BuildPersonalReferenceExplanation(PersonalReferenceProfileDetails profile)
    {
        return
            $"Answer-stage X-axis: accelerated-direct requires a structural direct path (path ratio 0.9 to {profile.DirectPathRatioMax:0.###}), " +
            "peak knob speed at or above the participant's same-distance P90, and correction rate at or below that bin's P25." +
            Environment.NewLine +
            "Hesitant-corrective requires at least two participant-relative signals: longer-than-reference path, above-reference correction rate, or slower-than-reference decision time." +
            Environment.NewLine +
            "Confidence is calculated separately as contextual evidence. These values produce review cues only; they do not make a careless/careful decision.";
    }

    private static string ToMetricDisplayName(string metricId) => metricId switch
    {
        "decisionRt" => "Decision time",
        "maxAbsVelocity" => "Peak knob speed",
        "pathRatio" => "Path ratio",
        "pauseRate" => "Pause rate",
        "reverseCount" => "Direction reversals",
        "microAdjustCount" => "Micro-adjustments",
        "correctionRate" => "Correction rate",
        _ => metricId
    };

    private static string ToDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unavailable";
        return string.Join(" ", value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string FormatReferenceValue(double? value) => value.HasValue
        ? value.Value.ToString("0.###")
        : "--";

    private static string FormatPercent(float value) => value < 0f ? "--" : $"{value * 100f:0}%";

    private static string FormatSlotDistance(int minimum, int maximum)
    {
        if (minimum < 0 || maximum < minimum)
            return "--";
        return minimum == maximum ? $"{minimum} slot" : $"{minimum}-{maximum} slots";
    }

    private void PopulateFileList(IReadOnlyList<FileInfo> files)
    {
        _fileList.BeginUpdate();
        try
        {
            _fileList.Items.Clear();
            foreach (FileInfo file in files)
            {
                var item = new ListViewItem(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(file.Extension.TrimStart('.').ToUpperInvariant());
                item.SubItems.Add(FormatBytes(file.Length));
                item.SubItems.Add(file.FullName);
                item.Tag = file.FullName;
                _fileList.Items.Add(item);
            }
        }
        finally
        {
            _fileList.EndUpdate();
        }
    }

    private void OpenSelectedFileLocation()
    {
        if (_fileList.SelectedItems.Count == 0 ||
            _fileList.SelectedItems[0].Tag is not string path ||
            !File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    private void BrowseOutputRoot(object? sender, EventArgs eventArgs)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the Unity experiment data output root",
            SelectedPath = Directory.Exists(_outputInput.Text) ? _outputInput.Text : _project.DefaultOutputRoot,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputInput.Text = dialog.SelectedPath;
    }

    private void ShowConsolePage(Panel page, Button selectedNavigation)
    {
        if (page == null)
            return;
        foreach (Panel candidate in new[] { _controlPage, _dataPage, _personalReferencePage, _probePage, _reviewPage })
        {
            if (candidate != null)
                candidate.Visible = candidate == page;
        }
        page.BringToFront();
        if (page.AutoScroll)
            page.AutoScrollPosition = Point.Empty;
        foreach (Button navigation in new[] { _sceneControlNav, _personalReferenceNav, _participantDataNav, _probeDefinitionNav, _evidenceReviewNav })
        {
            if (navigation == null)
                continue;
            navigation.BackColor = navigation == selectedNavigation ? Accent : Sidebar;
            navigation.ForeColor = Color.White;
        }
    }

    private void ShowFirstVersionNotice(string message)
    {
        SetStatus(message, Warning);
        MessageBox.Show(this, message, "First-version boundary", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowEntryView()
    {
        _consoleView.Visible = false;
        _entryView.Visible = true;
        _entryView.BringToFront();
    }

    private void ResetConsoleState()
    {
        _headerContext.Text = "No participant";
        _participantValue.Text = "—";
        _currentSceneValue.Text = "None";
        _currentSceneDetail.Text = "Waiting for researcher";
        _calibrationValue.Text = "0 / 4";
        _calibrationDetail.Text = "Not started";
        _pipelineBadge.Text = "Pipeline ready";
        _calibrationProgress.Text = "0 / 4 collected";
        _profileTitle.Text = "Calibration bundle · unavailable";
        _profileDetail.Text = "A personal knob profile, Baseline plus three demand-probe blocks, and a selected Probe Plugin are required before this bundle can be frozen.";
        _freezeProfileButton.Enabled = false;
        _combinedLaunchButton.Enabled = false;
        _combinedLaunchButton.Text = "Requires calibration";
        _dataScope.Text = "Active data scope: —";
        _fileList.Items.Clear();
        _activeProbePlugin = new ProbeRuleCardSet();
        _activeProbePluginPath = "";
        _contextualRecords = Array.Empty<ContextualResponseRecord>();
        ResetPersonalReferencePage();
        if (_probePluginList != null)
            _probePluginList.Items.Clear();
        if (_matrixDetail != null)
            _matrixDetail.Text = "Build a contextual record, then select a populated matrix cell to inspect its linked raw evidence.";
        foreach ((string id, BlockRowView view) in _blockViews)
        {
            view.Order.Text = id == "baseline" ? "1" : "—";
            view.Status.Text = "Queued";
            view.Status.BackColor = Color.FromArgb(237, 240, 243);
            view.Status.ForeColor = Muted;
        }
        UpdateEvidenceMatrixStatus();
    }

    private void SetStatus(string message, Color? color = null)
    {
        _statusBar.Text = message;
        _statusBar.ForeColor = color ?? Muted;
    }

    private static Panel CreateBrandBlock(string title, string subtitle)
    {
        Panel panel = new() { Dock = DockStyle.Left, Width = 380, BackColor = Surface };
        Label mark = CreateLabel("P", 13F, Color.White, FontStyle.Bold);
        mark.Location = new Point(0, 0);
        mark.Size = new Size(38, 38);
        mark.TextAlign = ContentAlignment.MiddleCenter;
        mark.BackColor = Accent;
        panel.Controls.Add(mark);
        Label titleLabel = CreateLabel(title, 11F, Ink, FontStyle.Bold);
        titleLabel.Location = new Point(50, 0);
        titleLabel.Size = new Size(310, 23);
        panel.Controls.Add(titleLabel);
        Label subtitleLabel = CreateLabel(subtitle, 8.5F, Muted);
        subtitleLabel.Location = new Point(50, 23);
        subtitleLabel.Size = new Size(310, 20);
        panel.Controls.Add(subtitleLabel);
        return panel;
    }

    private static Control CreateReadinessRow(string label, string value)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 24,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        Label name = CreateLabel(label, 9F, Ink);
        name.Dock = DockStyle.Fill;
        name.TextAlign = ContentAlignment.MiddleLeft;
        Label state = CreateLabel($"✓  {value}", 9F, Ink);
        state.Dock = DockStyle.Fill;
        state.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(name, 0, 0);
        row.Controls.Add(state, 1, 0);
        return row;
    }

    private bool WorkflowCalibrationReady()
    {
        return (_snapshot?.CalibrationBundleReady ?? false) &&
               _activeProbePlugin.Find(ProbeDimensionCatalog.Mental.Id) is { Features.Count: > 0 } &&
               _activeProbePlugin.Find(ProbeDimensionCatalog.Physical.Id) is { Features.Count: > 0 } &&
               _activeProbePlugin.Find(ProbeDimensionCatalog.Temporal.Id) is { Features.Count: > 0 };
    }

    private static Panel CreateCard()
    {
        return new Panel
        {
            BackColor = Surface,
            Margin = Padding.Empty
        };
    }

    private static ListView CreateReferenceListView(Point location, Size size)
    {
        return new ListView
        {
            Location = location,
            Size = size,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
    }

    private static (Panel Card, Label Value, Label Detail) CreateStatCard(
        string label,
        string value,
        string detail)
    {
        Panel card = CreateCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(18, 14, 18, 12);
        Label labelControl = CreateLabel(label, 8.8F, Muted);
        labelControl.Dock = DockStyle.Top;
        labelControl.Height = 24;
        Label valueControl = CreateLabel(value, 17F, Ink, FontStyle.Bold);
        valueControl.Dock = DockStyle.Top;
        valueControl.Height = 38;
        Label detailControl = CreateLabel(detail, 8.5F, Muted);
        detailControl.Dock = DockStyle.Fill;
        card.Controls.Add(detailControl);
        card.Controls.Add(valueControl);
        card.Controls.Add(labelControl);
        return (card, valueControl, detailControl);
    }

    private static Label CreateFieldLabel(string text)
    {
        Label label = CreateLabel(text, 9F, Ink, FontStyle.Bold);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.BottomLeft;
        label.Margin = Padding.Empty;
        return label;
    }

    private static TextBox CreateTextBox(string placeholder = "")
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11F),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Surface,
            ForeColor = Ink,
            PlaceholderText = placeholder,
            Margin = new Padding(0, 0, 0, 2)
        };
    }

    private static Label CreateLabel(
        string text,
        float size,
        Color color,
        FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoSize = false
        };
    }

    private static Label CreateBadge(string text, Color background, Color foreground)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 8.8F, FontStyle.Bold),
            ForeColor = foreground,
            BackColor = background,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Padding = new Padding(8, 2, 8, 2)
        };
    }

    private static Button CreatePrimaryButton(string text, int width = 160)
    {
        Button button = CreateButton(text, width);
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 101, 95);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(14, 82, 77);
        return button;
    }

    private static Button CreateSecondaryButton(string text, int width = 140)
    {
        Button button = CreateButton(text, width);
        button.BackColor = Surface;
        button.ForeColor = Ink;
        button.FlatAppearance.BorderColor = Line;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 243, 246);
        return button;
    }

    private static Button CreateGhostButton(string text, int width = 110)
    {
        Button button = CreateButton(text, width);
        button.BackColor = Surface;
        button.ForeColor = Muted;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 243, 246);
        return button;
    }

    private static Button CreateButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
    }

    private static Button CreateNavigationButton(string text)
    {
        Button button = new()
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = Sidebar,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = new Font("Segoe UI", 9.2F),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.MouseEnter += (_, _) =>
        {
            if (button.BackColor == Sidebar)
                button.BackColor = Color.FromArgb(44, 58, 69);
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.BackColor != Accent)
                button.BackColor = Sidebar;
        };
        return button;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private sealed record BlockRowView(Label Order, Label Status, Label Meta);
}
