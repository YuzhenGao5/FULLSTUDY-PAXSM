namespace PAXSMResearcherConsole;

/// <summary>
/// Lets the researcher explicitly decide which completed calibration participants
/// are used to construct a reusable task-probe plugin. This prevents test runs or
/// unrelated sessions from silently entering a study-level direction estimate.
/// </summary>
internal sealed class ProbeCohortSelectionDialog : Form
{
    private readonly ListView _list = new();
    private readonly IReadOnlyList<ProbeCalibrationSnapshot> _sources;

    public ProbeCohortSelectionDialog(
        IReadOnlyList<ProbeCalibrationSnapshot> sources,
        IEnumerable<string> selectedRunDirectories)
    {
        _sources = sources;
        var selected = new HashSet<string>(selectedRunDirectories, StringComparer.OrdinalIgnoreCase);

        Text = "Choose Probe calibration cohort";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 520);
        MinimumSize = new Size(720, 420);
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(244, 247, 249);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 104,
            BackColor = Color.White,
            Padding = new Padding(24, 18, 24, 12)
        };
        Controls.Add(header);
        header.Controls.Add(new Label
        {
            Text = "Choose completed calibration participants",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 39, 49)
        });
        header.Controls.Add(new Label
        {
            Text = "Each row is the latest complete Baseline + Mental/Physical Probe run for one participant. Choose at least three participants before treating directions as a cohort-level plugin; fewer are saved as provisional.",
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 5, 0, 0),
            Font = new Font(Font.FontFamily, 8.8F),
            ForeColor = Color.FromArgb(91, 106, 119)
        });

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 14),
            BackColor = Color.FromArgb(244, 247, 249)
        };
        Controls.Add(body);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.GridLines = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = Color.White;
        _list.ForeColor = Color.FromArgb(28, 39, 49);
        _list.Columns.Add("Participant", 112);
        _list.Columns.Add("Available calibration blocks", 230);
        _list.Columns.Add("Run directory", 500);
        body.Controls.Add(_list);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Color.FromArgb(244, 247, 249)
        };
        body.Controls.Add(footer);
        var cancel = CreateButton("Cancel", Color.FromArgb(104, 118, 130));
        cancel.Dock = DockStyle.Right;
        cancel.Width = 92;
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        footer.Controls.Add(cancel);
        var use = CreateButton("Use selected calibrations", Color.FromArgb(23, 118, 111));
        use.Dock = DockStyle.Right;
        use.Width = 190;
        use.Margin = new Padding(0, 0, 10, 0);
        use.Click += (_, _) => DialogResult = DialogResult.OK;
        footer.Controls.Add(use);

        foreach (ProbeCalibrationSnapshot source in _sources
                     .OrderBy(item => item.ParticipantId, StringComparer.OrdinalIgnoreCase))
        {
            string blocks = string.Join(", ", source.Blocks.Keys
                .Where(id => id is "baseline" or "cognitive_heavy" or "physical_heavy")
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            var item = new ListViewItem(source.ParticipantId)
            {
                Checked = selected.Contains(source.RunDirectory),
                Tag = source
            };
            item.SubItems.Add(blocks);
            item.SubItems.Add(source.RunDirectory);
            _list.Items.Add(item);
        }
    }

    public IReadOnlyList<string> SelectedRunDirectories => _list.Items
        .Cast<ListViewItem>()
        .Where(item => item.Checked && item.Tag is ProbeCalibrationSnapshot)
        .Select(item => ((ProbeCalibrationSnapshot)item.Tag!).RunDirectory)
        .ToList();

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
}
