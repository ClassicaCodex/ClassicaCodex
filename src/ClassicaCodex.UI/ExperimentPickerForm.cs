using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Chooses a saved experiment to reload.
///
/// A list rather than a combo box because the useful information is in the
/// comparison: two rows differing only in their seed are the same experiment
/// repeated, which is worth opening side by side; two differing in sample size
/// are not comparable at all. The profile key puts that difference on screen
/// instead of leaving it to be remembered.
/// </summary>
public class ExperimentPickerForm : Form
{
    private readonly ListView _list;

    public ExperimentSummary? Selected { get; private set; }

    public ExperimentPickerForm(IReadOnlyList<ExperimentSummary> experiments)
    {
        Text = "Load a saved experiment";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 940;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;

        _list = new ListView
        {
            Left = 12, Top = 12, Width = 900, Height = 400,
            View = View.Details, FullRowSelect = true, MultiSelect = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _list.Columns.Add("Run", 130);
        _list.Columns.Add("Author", 100);
        _list.Columns.Add("Pool", 170);
        _list.Columns.Add("Samples", 70, HorizontalAlignment.Right);
        _list.Columns.Add("MFW", 55, HorizontalAlignment.Right);
        _list.Columns.Add("Seed", 55, HorizontalAlignment.Right);
        _list.Columns.Add("Iterations", 75, HorizontalAlignment.Right);
        _list.Columns.Add("Label", 220);
        ReadingTheme.EnableThemedHeader(_list);

        foreach (var e in experiments)
        {
            var row = new ListViewItem(new[]
            {
                e.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                e.TargetAuthor,
                e.PoolSummary,
                e.ChunkSize.ToString("N0"),
                e.FeatureWordCount.ToString(),
                e.Seed.ToString(),
                e.Iterations.ToString(),
                e.Label ?? ""
            })
            { Tag = e };

            _list.Items.Add(row);
        }

        if (_list.Items.Count > 0) _list.Items[0].Selected = true;

        var open = new Button
        {
            Text = "Open", Left = 692, Top = 424, Width = 100, Height = 30,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        var cancel = new Button
        {
            Text = "Cancel", Left = 800, Top = 424, Width = 100, Height = 30,
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        var delete = new Button
        {
            Text = "Delete", Left = 12, Top = 424, Width = 100, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        delete.Click += async (_, _) =>
        {
            if (_list.SelectedItems.Count == 0) return;
            if (_list.SelectedItems[0].Tag is not ExperimentSummary chosen) return;

            // Confirmed, because a perturbation sweep is thousands of mixtures
            // and several minutes, and there is no undo.
            var confirm = MessageBox.Show(this,
                $"Delete the experiment from {chosen.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}?" +
                Environment.NewLine + Environment.NewLine +
                "This cannot be undone, and re-running it takes as long as it did the first time.",
                "Delete experiment", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            await new StylometryExperimentRepository().DeleteAsync(chosen.ExperimentId);
            _list.Items.Remove(_list.SelectedItems[0]);
        };

        _list.DoubleClick += (_, _) =>
        {
            if (_list.SelectedItems.Count == 0) return;
            Selected = _list.SelectedItems[0].Tag as ExperimentSummary;
            DialogResult = DialogResult.OK;
            Close();
        };

        open.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count > 0)
                Selected = _list.SelectedItems[0].Tag as ExperimentSummary;
        };

        Controls.Add(_list);
        Controls.Add(delete);
        Controls.Add(open);
        Controls.Add(cancel);

        AcceptButton = open;
        CancelButton = cancel;

        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }
}
