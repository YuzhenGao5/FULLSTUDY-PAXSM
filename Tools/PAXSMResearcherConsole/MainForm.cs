using System.Diagnostics;
using System.Drawing.Drawing2D;

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
    private Button _sceneControlNav = null!;
    private Button _participantDataNav = null!;
    private Button _evidenceReviewNav = null!;

    private ResearchSession? _session;
    private DataSnapshot? _snapshot;
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
        _reviewPage = BuildReviewPage();
        _contentHost.Controls.Add(_reviewPage);
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

        _participantDataNav = CreateNavigationButton("Participant data");
        _participantDataNav.Click += async (_, _) =>
        {
            ShowConsolePage(_dataPage, _participantDataNav);
            await RefreshDataAsync();
        };
        sidebar.Controls.Add(_participantDataNav);
        _participantDataNav.BringToFront();

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

        Button probeDefinition = CreateNavigationButton("Probe definition");
        probeDefinition.Click += (_, _) => ShowFirstVersionNotice(
            "Probe configuration remains in Unity in this first version. The Console only reads its exported evidence.");
        sidebar.Controls.Add(probeDefinition);
        probeDefinition.BringToFront();

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
        Panel body = new() { Dock = DockStyle.Top, Height = 1040, BackColor = Page };
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
        (Panel calibrationCard, _calibrationValue, _calibrationDetail) = CreateStatCard("Calibration scene", "0 / 4", "Not started");
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
            Height = 244,
            BackColor = Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(16, 2, 16, 2)
        };
        body.Controls.Add(sceneList);
        body.Resize += (_, _) => sceneList.Width = body.ClientSize.Width;
        AddSceneRow(sceneList, SceneDefinitions.Comparison, 0, false);
        AddSceneRow(sceneList, SceneDefinitions.Workload, 80, true);
        AddSceneRow(sceneList, SceneDefinitions.Combined, 160, false);

        Label blockTitle = CreateLabel("Workload scene · internal block monitor", 13F, Ink, FontStyle.Bold);
        blockTitle.Location = new Point(0, 538);
        blockTitle.Size = new Size(480, 28);
        body.Controls.Add(blockTitle);
        Label blockHelp = CreateLabel(
            "All four rows belong to one XRWorkloadProbeScene. Status is read from Unity questionnaire checkpoints.",
            9F,
            Muted);
        blockHelp.Location = new Point(0, 566);
        blockHelp.Size = new Size(760, 24);
        body.Controls.Add(blockHelp);
        _calibrationProgress = CreateBadge("0 / 4 collected", Color.FromArgb(237, 240, 243), Muted);
        _calibrationProgress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _calibrationProgress.Location = new Point(900, 542);
        _calibrationProgress.Size = new Size(160, 28);
        body.Controls.Add(_calibrationProgress);
        body.Resize += (_, _) => _calibrationProgress.Left = Math.Max(
            blockTitle.Right + 12,
            body.ClientSize.Width - _calibrationProgress.Width);

        Panel blockList = new()
        {
            Location = new Point(0, 596),
            Width = 1060,
            Height = 280,
            BackColor = Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(16, 0, 16, 0)
        };
        body.Controls.Add(blockList);
        body.Resize += (_, _) => blockList.Width = body.ClientSize.Width;
        AddBlockRow(blockList, "baseline", "Baseline", "Fixed first", 0);
        AddBlockRow(blockList, "cognitive_heavy", "Cognitive-heavy", "Randomized inside scene", 70);
        AddBlockRow(blockList, "physical_heavy", "Physical-heavy", "Randomized inside scene", 140);
        AddBlockRow(blockList, "temporal_heavy", "Temporal-heavy", "Randomized inside scene", 210);

        Panel gate = new()
        {
            Location = new Point(0, 898),
            Width = 1060,
            Height = 96,
            BackColor = Surface,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(18, 16, 18, 12)
        };
        body.Controls.Add(gate);
        body.Resize += (_, _) => gate.Width = body.ClientSize.Width;
        _profileTitle = CreateLabel("CalibrationProfile · unavailable", 11F, Ink, FontStyle.Bold);
        _profileTitle.Location = new Point(18, 16);
        _profileTitle.Size = new Size(520, 24);
        gate.Controls.Add(_profileTitle);
        _profileDetail = CreateLabel(
            "Unity must report all four Workload blocks collected before this participant profile can be frozen.",
            9F,
            Muted);
        _profileDetail.Location = new Point(18, 44);
        _profileDetail.Size = new Size(720, 28);
        gate.Controls.Add(_profileDetail);
        _freezeProfileButton = CreatePrimaryButton("Freeze profile", 150);
        _freezeProfileButton.Enabled = false;
        _freezeProfileButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _freezeProfileButton.Location = new Point(876, 26);
        _freezeProfileButton.Click += FreezeProfile;
        gate.Controls.Add(_freezeProfileButton);
        gate.Resize += (_, _) => _freezeProfileButton.Left = gate.ClientSize.Width - _freezeProfileButton.Width - 18;

        _dataScope = CreateLabel("Active data scope: —", 8.8F, Muted);
        _dataScope.Location = new Point(0, 1010);
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

    private Panel BuildReviewPage()
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = Page };
        Label title = CreateLabel("Evidence review", 22F, Ink, FontStyle.Bold);
        title.Location = new Point(0, 0);
        title.Size = new Size(430, 34);
        page.Controls.Add(title);
        Label subtitle = CreateLabel(
            "First-version boundary: inspect collected evidence files without changing Unity raw data.",
            9.5F,
            Muted);
        subtitle.Location = new Point(0, 36);
        subtitle.Size = new Size(760, 28);
        page.Controls.Add(subtitle);

        Panel card = CreateCard();
        card.Location = new Point(0, 88);
        card.Size = new Size(760, 250);
        card.Padding = new Padding(24);
        page.Controls.Add(card);
        Label heading = CreateLabel("Review workflow placeholder", 14F, Ink, FontStyle.Bold);
        heading.Dock = DockStyle.Top;
        heading.Height = 34;
        card.Controls.Add(heading);
        Label body = CreateLabel(
            "The Console already preserves the participant context and discovers questionnaire, probe, behavior, and integrity files. " +
            "The next version can add the linked timeline and researcher annotations here.\r\n\r\n" +
            "Raw Unity CSV files remain read-only.",
            10F,
            Muted);
        body.Dock = DockStyle.Fill;
        card.Controls.Add(body);
        body.BringToFront();
        heading.BringToFront();
        return page;
    }

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
        if (scene == SceneDefinitions.Combined && !(_snapshot?.CalibrationComplete ?? false))
        {
            SetStatus("Combined remains locked until all four Workload blocks are collected.", Warning);
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
        if (_session == null || _snapshot == null || !_snapshot.CalibrationComplete)
            return;

        try
        {
            string directory = _project.ConsoleSessionDirectory(_session);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "calibration_profile_freeze.json");
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = "PAXSM_CalibrationProfileFreeze_v1",
                participantId = _session.ParticipantId,
                sessionNumber = _session.SessionNumber,
                frozenAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                source = "Unity Workload questionnaire checkpoints",
                rawDataModified = false,
                blocks = _snapshot.CalibrationBlocks.Select(block => new
                {
                    blockId = block.BlockId,
                    presentationOrder = block.PresentationOrder,
                    questionnaireItemCount = block.QuestionnaireItemCount
                })
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            _profileTitle.Text = "CalibrationProfile · frozen";
            _profileDetail.Text = "Freeze record saved separately; Unity raw data was not modified.";
            _freezeProfileButton.Enabled = false;
            _combinedLaunchButton.Enabled = true;
            _combinedLaunchButton.Text = "Launch scene";
            _calibrationDetail.Text = "Profile frozen";
            SetStatus($"Calibration freeze record saved: {path}", Success);
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
        int collected = snapshot.CalibrationBlocks.Count(block =>
            block.State == CalibrationBlockState.Collected);
        _calibrationValue.Text = $"{collected} / 4";
        _calibrationProgress.Text = $"{collected} / 4 collected";
        _calibrationDetail.Text = snapshot.CalibrationComplete
            ? "Ready to freeze"
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

        if (snapshot.CalibrationComplete)
        {
            _profileTitle.Text = "CalibrationProfile · ready to freeze";
            _profileDetail.Text = "All four blocks were found in Unity questionnaire exports.";
            _freezeProfileButton.Enabled = true;
        }

        _combinedLaunchButton.Enabled = snapshot.CalibrationComplete;
        _combinedLaunchButton.Text = snapshot.CalibrationComplete ? "Launch scene" : "Requires calibration";
        _dataFileCount.Text = (snapshot.CsvFileCount + snapshot.JsonFileCount).ToString();
        _dataRunCount.Text = snapshot.Runs.Count.ToString();
        _dataLatest.Text = snapshot.LatestWriteUtc.HasValue
            ? snapshot.LatestWriteUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "—";
        PopulateFileList(snapshot.LatestFiles);
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
        foreach (Panel candidate in new[] { _controlPage, _dataPage, _reviewPage })
        {
            if (candidate != null)
                candidate.Visible = candidate == page;
        }
        page.BringToFront();
        foreach (Button navigation in new[] { _sceneControlNav, _participantDataNav, _evidenceReviewNav })
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
        _profileTitle.Text = "CalibrationProfile · unavailable";
        _profileDetail.Text = "Unity must report all four Workload blocks collected before this participant profile can be frozen.";
        _freezeProfileButton.Enabled = false;
        _combinedLaunchButton.Enabled = false;
        _combinedLaunchButton.Text = "Requires calibration";
        _dataScope.Text = "Active data scope: —";
        _fileList.Items.Clear();
        foreach ((string id, BlockRowView view) in _blockViews)
        {
            view.Order.Text = id == "baseline" ? "1" : "—";
            view.Status.Text = "Queued";
            view.Status.BackColor = Color.FromArgb(237, 240, 243);
            view.Status.ForeColor = Muted;
        }
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

    private static Panel CreateCard()
    {
        return new Panel
        {
            BackColor = Surface,
            Margin = Padding.Empty
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
