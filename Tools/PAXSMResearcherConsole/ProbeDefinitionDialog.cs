namespace PAXSMResearcherConsole;

/// <summary>
/// Researcher-facing construction step for the task-probe plugin. The dialog
/// never changes Unity task data: it only persists the selected metric IDs and
/// calibration-derived directions as a separately auditable definition.
/// </summary>
internal sealed class ProbeDefinitionDialog : Form
{
    private readonly ResearchSession _session;
    private readonly DataSnapshot _snapshot;
    private readonly ProbeCalibrationReader _reader = new();
    private readonly ProbeRuleCardSet _cards;
    private readonly ProbeCalibrationSnapshot? _activeCalibration;
    private readonly ProbeCalibrationCohort _cohort;
    private readonly IReadOnlyList<ProbeCalibrationSnapshot> _availableCalibrations;
    private readonly HashSet<string> _selectedCalibrationRunDirectories =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox _dimensionSelect = new();
    private readonly DataGridView _grid = new();
    private readonly Label _sourceLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _definitionSummary = new();
    private readonly TextBox _pluginName = new();
    private List<ProbeMetricCandidate> _candidates = new();

    private static readonly Color Ink = Color.FromArgb(28, 39, 49);
    private static readonly Color Muted = Color.FromArgb(91, 106, 119);
    private static readonly Color Accent = Color.FromArgb(23, 118, 111);
    private static readonly Color Page = Color.FromArgb(244, 247, 249);

    public ProbeDefinitionDialog(ProjectServices project, ResearchSession session, DataSnapshot snapshot)
    {
        _session = session;
        _snapshot = snapshot;
        _cards = ProbeRuleCardStore.Load(session.OutputRoot);
        _activeCalibration = _reader.LoadLatest(snapshot, session);
        _cohort = _reader.LoadCohort(session.OutputRoot);
        _availableCalibrations = _cohort.Participants;
        foreach (ProbeCalibrationSource source in _cards.CalibrationSources)
            _selectedCalibrationRunDirectories.Add(source.RunDirectory);
        if (_selectedCalibrationRunDirectories.Count == 0 && _activeCalibration != null)
            _selectedCalibrationRunDirectories.Add(_activeCalibration.RunDirectory);

        Text = "CARE-XR Probe Definition";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 760);
        MinimumSize = new Size(960, 640);
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Page;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        _dimensionSelect.SelectedIndex = 0;
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 132,
            BackColor = Color.White,
            Padding = new Padding(28, 22, 28, 16)
        };
        Controls.Add(header);

        var title = new Label
        {
            Text = "Task Probe definition",
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            ForeColor = Ink,
            Dock = DockStyle.Top,
            Height = 32
        };
        header.Controls.Add(title);
        var description = new Label
        {
            Text = "Choose two or three behavioral metrics whose within-participant calibration change gives useful task-context evidence. This adds a calibrated dimension to a reusable Probe Plugin; it does not label a participant or infer a cognitive state.",
            Font = new Font(Font.FontFamily, 9F),
            ForeColor = Muted,
            Dock = DockStyle.Top,
            Height = 45,
            Padding = new Padding(0, 6, 0, 0)
        };
        header.Controls.Add(description);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 18), BackColor = Page };
        Controls.Add(body);

        var pluginLabel = new Label
        {
            Text = "Probe Plugin",
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            ForeColor = Ink,
            Location = new Point(0, 0),
            Size = new Size(100, 24)
        };
        body.Controls.Add(pluginLabel);
        _pluginName.Location = new Point(104, 0);
        _pluginName.Size = new Size(240, 28);
        _pluginName.Text = _cards.PluginName;
        _pluginName.BorderStyle = BorderStyle.FixedSingle;
        body.Controls.Add(_pluginName);

        var dimensionLabel = new Label
        {
            Text = "Target NASA-TLX dimension",
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            ForeColor = Ink,
            Location = new Point(366, 0),
            Size = new Size(210, 24)
        };
        body.Controls.Add(dimensionLabel);
        _dimensionSelect.Location = new Point(580, 0);
        _dimensionSelect.Size = new Size(230, 30);
        _dimensionSelect.DropDownStyle = ComboBoxStyle.DropDownList;
        _dimensionSelect.Items.AddRange(ProbeDimensionCatalog.All.Cast<object>().ToArray());
        _dimensionSelect.DisplayMember = nameof(ProbeDimensionDefinition.DisplayName);
        _dimensionSelect.SelectedIndexChanged += (_, _) => ReloadDimension();
        body.Controls.Add(_dimensionSelect);

        var chooseCohort = CreateButton("Choose calibration cohort", Color.FromArgb(79, 98, 111));
        chooseCohort.Location = new Point(846, 0);
        chooseCohort.Size = new Size(256, 30);
        chooseCohort.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chooseCohort.Click += (_, _) => ChooseCalibrationCohort();
        body.Controls.Add(chooseCohort);

        _sourceLabel.Location = new Point(0, 40);
        _sourceLabel.Size = new Size(1100, 38);
        _sourceLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _sourceLabel.Font = new Font(Font.FontFamily, 8.7F);
        _sourceLabel.ForeColor = Muted;
        body.Controls.Add(_sourceLabel);

        _grid.Location = new Point(0, 88);
        _grid.Size = new Size(1120, 420);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 29;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(236, 242, 246),
            ForeColor = Ink,
            Font = new Font(Font.FontFamily, 8.6F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        _grid.EnableHeadersVisualStyles = false;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Use", Width = 46 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Metric", HeaderText = "Behavior metric", Width = 210, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Baseline", HeaderText = "Baseline", Width = 95, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Condition", HeaderText = "Calibration", Width = 95, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Delta", HeaderText = "Change", Width = 96, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Agreement", HeaderText = "Direction agreement", Width = 128, ReadOnly = true });
        var direction = new DataGridViewComboBoxColumn { Name = "Direction", HeaderText = "Expected direction", Width = 160 };
        direction.Items.AddRange("Higher than baseline", "Lower than baseline");
        _grid.Columns.Add(direction);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Unit", HeaderText = "Unit", Width = 78, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Metric ID", Width = 170, ReadOnly = true });
        _grid.CellContentClick += (_, eventArgs) =>
        {
            if (_grid.Columns["Use"] is DataGridViewColumn useColumn &&
                eventArgs.ColumnIndex == useColumn.Index && eventArgs.RowIndex >= 0)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        body.Controls.Add(_grid);

        _definitionSummary.Location = new Point(0, 522);
        _definitionSummary.Size = new Size(760, 48);
        _definitionSummary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _definitionSummary.Font = new Font(Font.FontFamily, 8.7F);
        _definitionSummary.ForeColor = Muted;
        body.Controls.Add(_definitionSummary);

        _statusLabel.Location = new Point(0, 574);
        _statusLabel.Size = new Size(760, 26);
        _statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.Font = new Font(Font.FontFamily, 8.7F);
        _statusLabel.ForeColor = Muted;
        body.Controls.Add(_statusLabel);

        var save = CreateButton("Save Plugin dimension", Accent);
        save.Size = new Size(158, 38);
        save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        save.Location = new Point(962, 562);
        save.Click += (_, _) => SaveRuleCard();
        body.Controls.Add(save);
        var cancel = CreateButton("Close", Color.FromArgb(104, 118, 130));
        cancel.Size = new Size(92, 38);
        cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cancel.Location = new Point(858, 562);
        cancel.Click += (_, _) => Close();
        body.Controls.Add(cancel);
    }

    private void ReloadDimension()
    {
        if (_dimensionSelect.SelectedItem is not ProbeDimensionDefinition definition)
            return;

        IReadOnlyList<ProbeCalibrationSnapshot> selectedSources = SelectedCalibrationSources();
        bool useCohort = selectedSources.Count >= 3;
        if (useCohort)
        {
            _candidates = _reader.GetCohortCandidates(
                new ProbeCalibrationCohort { Participants = selectedSources },
                definition).ToList();
            _sourceLabel.Text = $"Selected calibration cohort: {selectedSources.Count} participants. Direction is the median of condition minus Baseline within each person; agreement shows how consistently the direction recurred.";
        }
        else if (selectedSources.Count == 1)
        {
            _candidates = _reader.GetCandidates(selectedSources[0], definition).ToList();
            _sourceLabel.Text = $"Selected calibration source: {selectedSources[0].ParticipantId}. n=1 is provisional; use the cohort chooser to select at least three completed calibration participants for a study-level Plugin.";
        }
        else
        {
            _candidates = new List<ProbeMetricCandidate>();
            _sourceLabel.Text = "No calibration source is selected. Choose at least one completed Baseline + Probe calibration run.";
        }

        ProbeDimensionRuleCard? existing = _cards.Find(definition.Id);
        var existingDirections = (existing?.Features ?? new List<ProbeFeatureRule>())
            .ToDictionary(feature => feature.MetricId, feature => feature.ExpectedDirection, StringComparer.OrdinalIgnoreCase);
        _grid.Rows.Clear();
        foreach (ProbeMetricCandidate candidate in _candidates)
        {
            bool selected = existingDirections.TryGetValue(candidate.MetricId, out string? existingDirection);
            string direction = existingDirection == "lower" || (!selected && candidate.Delta < 0d)
                ? "Lower than baseline"
                : "Higher than baseline";
            string agreement = candidate.SourceParticipantCount > 1
                ? $"{candidate.DirectionAgreement:P0} of n={candidate.SourceParticipantCount}"
                : "n=1 provisional";
            int index = _grid.Rows.Add(
                selected,
                candidate.MetricName,
                FormatNumber(candidate.BaselineValue),
                FormatNumber(candidate.CalibrationValue),
                FormatSignedNumber(candidate.Delta),
                agreement,
                direction,
                candidate.Unit,
                candidate.MetricId);
            _grid.Rows[index].Tag = candidate;
        }

        _definitionSummary.Text = existing == null
            ? "This Plugin has no saved definition for this dimension. Choose 2-3 metrics that you judge to be interpretable task-context evidence."
            : $"Saved Plugin dimension: {existing.Features.Count} metric(s), scope: {existing.Scope.Replace('_', ' ')}. Re-save to replace it.";
        _statusLabel.Text = _candidates.Count == 0
            ? "Choose completed Workload Probe calibrations before constructing this Plugin dimension."
            : "The Console preserves the calibration values, selected directions, and source paths in the saved Plugin.";
    }

    private void ChooseCalibrationCohort()
    {
        using var dialog = new ProbeCohortSelectionDialog(_availableCalibrations, _selectedCalibrationRunDirectories);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _selectedCalibrationRunDirectories.Clear();
        foreach (string directory in dialog.SelectedRunDirectories)
            _selectedCalibrationRunDirectories.Add(directory);
        ReloadDimension();
    }

    private IReadOnlyList<ProbeCalibrationSnapshot> SelectedCalibrationSources()
    {
        List<ProbeCalibrationSnapshot> selected = _availableCalibrations
            .Where(source => _selectedCalibrationRunDirectories.Contains(source.RunDirectory))
            .ToList();
        if (selected.Count > 0)
            return selected;
        return _activeCalibration == null
            ? Array.Empty<ProbeCalibrationSnapshot>()
            : new[] { _activeCalibration };
    }

    private void SaveRuleCard()
    {
        if (_dimensionSelect.SelectedItem is not ProbeDimensionDefinition definition)
            return;

        List<(ProbeMetricCandidate Candidate, string Direction)> selected = _grid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Tag is ProbeMetricCandidate && Convert.ToBoolean(row.Cells["Use"].Value ?? false))
            .Select(row => ((ProbeMetricCandidate)row.Tag!,
                row.Cells["Direction"].Value?.ToString() == "Lower than baseline" ? "lower" : "higher"))
            .ToList();
        if (selected.Count < 2 || selected.Count > 3)
        {
            _statusLabel.ForeColor = Color.FromArgb(172, 53, 53);
            _statusLabel.Text = "Select two or three metrics. A Probe is a small interpretable behavior combination, not a single unverified feature.";
            return;
        }

        IReadOnlyList<ProbeCalibrationSnapshot> selectedSources = SelectedCalibrationSources();
        ProbeCalibrationSnapshot? referenceSource = selectedSources.FirstOrDefault();
        ProbeMetricBlock? baseline = referenceSource?.Blocks.GetValueOrDefault("baseline");
        ProbeMetricBlock? condition = referenceSource?.Blocks.GetValueOrDefault(definition.CalibrationBlockId);
        bool cohortScoped = selectedSources.Count >= 3;
        var card = new ProbeDimensionRuleCard
        {
            DimensionId = definition.Id,
            DisplayName = definition.DisplayName,
            CalibrationBlockId = definition.CalibrationBlockId,
            CalibrationBlockName = definition.CalibrationBlockName,
            SourceParticipantId = cohortScoped ? $"calibration cohort (n={selectedSources.Count})" : referenceSource?.ParticipantId ?? _session.ParticipantId,
            CalibrationRunDirectory = referenceSource?.RunDirectory ?? "cohort summary",
            BaselineMetricsPath = baseline?.FilePath ?? "participant-specific baseline resolved at target run",
            ConditionMetricsPath = condition?.FilePath ?? "participant-specific condition resolved at target run",
            Scope = cohortScoped ? "calibration_cohort" : "provisional_participant_calibration",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Features = selected.Select(item => new ProbeFeatureRule
            {
                MetricId = item.Candidate.MetricId,
                MetricName = item.Candidate.MetricName,
                Unit = item.Candidate.Unit,
                ExpectedDirection = item.Direction,
                CalibrationBaselineValue = item.Candidate.BaselineValue,
                CalibrationConditionValue = item.Candidate.CalibrationValue,
                CalibrationDelta = item.Candidate.Delta
            }).ToList()
        };

        _cards.PluginName = string.IsNullOrWhiteSpace(_pluginName.Text)
            ? "Untitled task-probe plugin"
            : _pluginName.Text.Trim();
        _cards.CalibrationParticipantCount = selectedSources.Select(source => source.ParticipantId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        _cards.CalibrationSources = selectedSources.Select(_reader.CreateSource).ToList();
        _cards.Dimensions.RemoveAll(existing => existing.DimensionId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
        _cards.Dimensions.Add(card);
        string path = ProbeRuleCardStore.Save(_session.OutputRoot, _cards);
        _statusLabel.ForeColor = Color.FromArgb(30, 121, 77);
        _statusLabel.Text = $"Saved {definition.DisplayName} Plugin dimension: {path}";
        _definitionSummary.Text = $"Saved {card.Features.Count} selected metric(s). The Evidence Matrix will compare each target run against that participant's own Baseline using these frozen directions.";
    }

    private static Button CreateButton(string text, Color color) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        BackColor = color,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        Cursor = Cursors.Hand
    };

    private static string FormatNumber(double value) => double.IsNaN(value) ? "-" : value.ToString("0.###");
    private static string FormatSignedNumber(double value) => double.IsNaN(value) ? "-" : value.ToString("+0.###;-0.###;0");
}
