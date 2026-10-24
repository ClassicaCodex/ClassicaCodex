using Avalonia;
using Avalonia.Controls;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>Selects a saved Delta run to attach as durable research evidence.</summary>
internal sealed class StylometryRunPickerWindow : ResearchWindow
{
    private readonly DataGrid _runs = Ui.Table();

    public StylometryRunPickerWindow(IReadOnlyList<StylometryRunSummary> runs) : base(null)
    {
        Title = "Attach saved stylometry run";
        Width = 930;
        Height = 510;
        MinWidth = 640;
        MinHeight = 360;

        var explanation = Ui.Muted("Choose a run for this work. Its settings, metrics, neighbours, and fingerprint will be copied into evidence; the saved run remains unchanged.");
        explanation.Margin = new Thickness(16, 14, 16, 10);

        _runs.Columns.Add(Ui.Column("Saved", nameof(RunRow.Saved), 150));
        _runs.Columns.Add(Ui.Column("Label", nameof(RunRow.Label), 180));
        _runs.Columns.Add(Ui.Column("Settings profile", nameof(RunRow.Settings)));
        _runs.Columns.Add(Ui.Column("Pool", nameof(RunRow.Pool), 75));
        var rows = runs.OrderByDescending(r => r.CreatedUtc).Select(r => new RunRow(r)).ToList();
        _runs.ItemsSource = rows;
        _runs.SelectedItem = rows.FirstOrDefault();
        _runs.Margin = new Thickness(16, 0, 16, 12);
        Ui.OnRowDoubleTapped(_runs, () =>
        {
            Attach();
            return Task.CompletedTask;
        });

        var cancel = Ui.Button("Cancel");
        var attach = Ui.Button("Attach", accent: true);
        cancel.IsCancel = true;
        attach.IsDefault = true;
        cancel.Click += (_, _) => Close();
        attach.Click += (_, _) => Attach();

        var bar = Ui.ButtonBar(null, cancel, attach);
        var dock = new DockPanel();
        DockPanel.SetDock(explanation, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(explanation);
        dock.Children.Add(bar);
        dock.Children.Add(_runs);
        Content = dock;
    }

    /// <summary>The run chosen, or null if the reader cancelled.</summary>
    public StylometryRunSummary? SelectedRun { get; private set; }

    private void Attach()
    {
        if (_runs.SelectedItem is not RunRow row) return;
        SelectedRun = row.Run;
        Close();
    }

    private sealed class RunRow
    {
        public RunRow(StylometryRunSummary run) => Run = run;

        public StylometryRunSummary Run { get; }
        public string Saved => Run.CreatedUtc.ToLocalTime().ToString("g");
        public string Label => Run.Label ?? string.Empty;
        public string Settings => Run.Settings.Describe();
        public string Pool => Run.PoolSize.ToString("N0");
    }
}
