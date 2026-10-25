using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Chooses a saved experiment to reload.
///
/// A list rather than a pop-up menu because the useful information is in the
/// comparison: two rows differing only in their seed are the same experiment
/// repeated, which is worth opening side by side; two differing in sample size
/// are not comparable at all. The columns put that difference on screen
/// instead of leaving it to be remembered.
///
/// The Mac counterpart of ExperimentPickerForm, shown as a modal sheet the
/// way an Open panel is.
/// </summary>
public sealed class ExperimentPickerWindow : FeatureWindow
{
    private readonly StylometryExperimentRepository _experiments = new();
    private readonly DataGrid _list;
    private readonly List<ExperimentSummary> _rows;

    public ExperimentPickerWindow(IReadOnlyList<ExperimentSummary> experiments)
    {
        _rows = experiments.ToList();

        Title = "Load a saved experiment";
        Width = 980;
        Height = 540;
        MinWidth = 720;
        MinHeight = 360;
        CanMinimize = false;
        ShowInTaskbar = false;

        _list = BenchTable.Create(framed: true);
        _list.SelectionMode = DataGridSelectionMode.Single;
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Run", e => e.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture), e => e.CreatedUtc, 140));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Author", e => e.TargetAuthor, width: 120));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Pool", e => e.PoolSummary, star: true));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Samples", e => e.ChunkSize.ToString("N0", CultureInfo.CurrentCulture), e => e.ChunkSize, 84, numeric: true));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("MFW", e => e.FeatureWordCount.ToString(CultureInfo.CurrentCulture), e => e.FeatureWordCount, 64, numeric: true));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Seed", e => e.Seed.ToString(CultureInfo.CurrentCulture), e => e.Seed, 70, numeric: true));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Iterations", e => e.Iterations.ToString(CultureInfo.CurrentCulture), e => e.Iterations, 88, numeric: true));
        _list.Columns.Add(BenchTable.Column<ExperimentSummary>("Label", e => e.Label ?? "", width: 1.2, star: true));
        _list.ItemsSource = _rows;
        if (_rows.Count > 0) _list.SelectedIndex = 0;

        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is ExperimentSummary chosen) Choose(chosen);
        };

        TableExport.Attach(
            _list,
            () => "saved-experiments",
            () => ExportTable(_rows),
            () => ExportTable(_list.SelectedItems.OfType<ExperimentSummary>()),
            () => new[] { $"Classica Codex saved perturbation experiments - {DateTime.Now:yyyy-MM-dd HH:mm}" });

        var open = new Button { Content = "Open", Classes = { "accent" }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        var delete = Bench.Button("Delete…", "Trash", "Delete the selected experiment from the library");

        open.Click += (_, _) =>
        {
            if (_list.SelectedItem is ExperimentSummary chosen) Choose(chosen);
        };
        cancel.Click += (_, _) => Close();
        delete.Click += async (_, _) => await DeleteSelectedAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, open } };
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        delete.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(delete);

        var intro = new TextBlock
        {
            Text = "Saved perturbation experiments, newest first. Two rows that differ only in their seed are the same " +
                   "experiment repeated; two that differ in sample size or pool are not comparable at all.",
            Classes = { "muted" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var layout = new DockPanel { Margin = new Thickness(20, 18, 20, 18) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(intro);
        layout.Children.Add(bottom);
        layout.Children.Add(_list);
        Content = layout;

        Opened += (_, _) => _list.Focus();
    }

    /// <summary>The experiment chosen, or null if the reader cancelled.</summary>
    public ExperimentSummary? Selected { get; private set; }

    internal IReadOnlyList<ExperimentSummary> Rows => _rows;

    /// <summary>Shows the picker over <paramref name="owner"/> and returns the experiment chosen.</summary>
    public static async Task<ExperimentSummary?> ChooseAsync(Window owner, IReadOnlyList<ExperimentSummary> experiments)
    {
        var picker = new ExperimentPickerWindow(experiments);
        await picker.ShowDialog(owner);
        return picker.Selected;
    }

    internal void Choose(ExperimentSummary chosen)
    {
        Selected = chosen;
        Close();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_list.SelectedItem is not ExperimentSummary chosen) return;

        // Confirmed, because a perturbation sweep is thousands of mixtures
        // and several minutes, and there is no undo.
        var confirmed = await Alert.Confirm(this, "Delete experiment",
            $"Delete the experiment from {chosen.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}?" +
            Environment.NewLine + Environment.NewLine +
            "This cannot be undone, and re-running it takes as long as it did the first time.",
            "Delete", kind: AlertKind.Warning);

        if (!confirmed) return;

        try
        {
            await Task.Run(() => _experiments.DeleteAsync(chosen.ExperimentId));
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Delete failed", ex.Message);
            return;
        }

        _rows.Remove(chosen);
        _list.ItemsSource = _rows.ToList();
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExportTable(IEnumerable<ExperimentSummary> rows)
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "ExperimentId", "CreatedUtc", "Author", "Pool", "ChunkSize", "MFW", "FoldAccents", "Seed", "Iterations", "ProfileKey", "Label" }
        };

        table.AddRange(rows.Select(e => (IReadOnlyList<string>)new[]
        {
            e.ExperimentId.ToString(CultureInfo.InvariantCulture),
            e.CreatedUtc.ToString("O", CultureInfo.InvariantCulture),
            e.TargetAuthor,
            e.PoolSummary,
            e.ChunkSize.ToString(CultureInfo.InvariantCulture),
            e.FeatureWordCount.ToString(CultureInfo.InvariantCulture),
            e.FoldAccents ? "yes" : "no",
            e.Seed.ToString(CultureInfo.InvariantCulture),
            e.Iterations.ToString(CultureInfo.InvariantCulture),
            e.ProfileKey,
            e.Label ?? ""
        }));

        return table;
    }
}
