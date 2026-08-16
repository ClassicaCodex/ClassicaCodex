using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Selects a saved Delta run to attach as durable research evidence.</summary>
public sealed class StylometryRunEvidencePickerForm : Form
{
    private readonly ListView _runs = new();

    public StylometryRunSummary? SelectedRun { get; private set; }

    public StylometryRunEvidencePickerForm(IReadOnlyList<StylometryRunSummary> runs)
    {
        Text = "Attach saved stylometry run";
        Width = 930;
        Height = 510;
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "Stylometry");

        var explanation = new Label
        {
            Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 10, 8, 0),
            Text = "Choose a run for this work. Its settings, metrics, neighbours, and fingerprint will be copied into evidence; the saved run remains unchanged."
        };
        _runs.Dock = DockStyle.Fill;
        _runs.View = View.Details;
        _runs.FullRowSelect = true;
        _runs.MultiSelect = false;
        _runs.Columns.Add("Saved", 145);
        _runs.Columns.Add("Label", 180);
        _runs.Columns.Add("Settings profile", 360);
        _runs.Columns.Add("Pool", 75, HorizontalAlignment.Right);
        ReadingTheme.EnableThemedHeader(_runs);

        foreach (var run in runs.OrderByDescending(r => r.CreatedUtc))
        {
            _runs.Items.Add(new ListViewItem(new[]
            {
                run.CreatedUtc.ToLocalTime().ToString("g"),
                run.Label ?? "",
                run.Settings.Describe(),
                run.PoolSize.ToString("N0")
            }) { Tag = run });
        }
        if (_runs.Items.Count > 0) _runs.Items[0].Selected = true;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        var attach = new Button { Text = "Attach", Width = 90, DialogResult = DialogResult.OK };
        attach.Click += (_, _) => SelectedRun = _runs.SelectedItems.Count == 0
            ? null : _runs.SelectedItems[0].Tag as StylometryRunSummary;
        _runs.DoubleClick += (_, _) =>
        {
            if (_runs.SelectedItems.Count == 0) return;
            SelectedRun = _runs.SelectedItems[0].Tag as StylometryRunSummary;
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(attach);

        Controls.Add(_runs);
        Controls.Add(buttons);
        Controls.Add(explanation);
        AcceptButton = attach;
        CancelButton = cancel;
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }
}
