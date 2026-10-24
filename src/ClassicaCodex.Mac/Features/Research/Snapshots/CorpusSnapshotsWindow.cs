using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Snapshots;

/// <summary>
/// Frozen fingerprints of the corpus a project's findings were drawn from -
/// which works, which editions, how many lines, and a hash of their text - so
/// a later reader can check whether the library has changed underneath them.
/// </summary>
internal sealed class CorpusSnapshotsWindow : ResearchWindow
{
    private readonly ResearchProject _project;
    private readonly ResearchCorpusSnapshotRepository _repo = new();
    private readonly ListBox _snapshots;
    private readonly DataGrid _details = Ui.Table();
    private readonly TextBox _name = Ui.Line();
    private readonly TextBox _notes = Ui.Area(52);
    private readonly ComboBox _scope = Ui.Combo(new[]
    {
        new Choice<CorpusSnapshotScope>(CorpusSnapshotScope.ProjectWork, "This work only"),
        new Choice<CorpusSnapshotScope>(CorpusSnapshotScope.SameAuthor, "All works by this author"),
        new Choice<CorpusSnapshotScope>(CorpusSnapshotScope.EntireCorpus, "Entire installed corpus")
    });
    private readonly TextBlock _status = Ui.Status();
    private readonly Button _capture = Ui.Button("Capture Snapshot", "Camera", accent: true);
    private readonly Button _compare = Ui.Button("Compare with Current", "Validation");
    private readonly Button _cancel = Ui.Button("Cancel");
    private CancellationTokenSource? _operation;

    public CorpusSnapshotsWindow(ResearchProject project) : base(null)
    {
        _project = project;
        Title = $"Corpus Snapshots — {project.Name}";
        Width = 1180;
        Height = 760;
        MinWidth = 850;
        MinHeight = 560;

        _snapshots = Ui.TextList<ResearchCorpusSnapshot>(s => s.Name, s => $"{Words.Of(s.Scope)} · {s.EditionCount} editions · {s.CreatedUtc.ToLocalTime():g}");
        _snapshots.SelectionChanged += (_, _) => Run(ShowSnapshotAsync);

        _capture.Click += (_, _) => Run(CaptureAsync);
        _cancel.IsEnabled = false;
        _cancel.Click += (_, _) => _operation?.Cancel();
        _compare.Click += (_, _) => Run(CompareAsync);
        var remove = Ui.Button("Remove", "Minus");
        remove.Click += (_, _) => Run(RemoveAsync);

        var capturePanel = new Grid { ColumnDefinitions = new ColumnDefinitions("300,14,220,14,*"), Margin = new Thickness(16, 14, 16, 6) };
        var nameField = Ui.Field("Snapshot name", _name, 0);
        var scopeField = Ui.Field("Corpus scope", _scope, 0);
        var notesField = Ui.Field("Notes / purpose", _notes, 0);
        Grid.SetColumn(nameField, 0);
        Grid.SetColumn(scopeField, 2);
        Grid.SetColumn(notesField, 4);
        capturePanel.Children.Add(nameField);
        capturePanel.Children.Add(scopeField);
        capturePanel.Children.Add(notesField);

        var captureRow = new DockPanel { Margin = new Thickness(16, 6, 16, 12) };
        var captureButtons = Ui.Row(_capture, _cancel);
        DockPanel.SetDock(captureButtons, Dock.Left);
        _status.Margin = new Thickness(14, 0, 0, 0);
        captureRow.Children.Add(captureButtons);
        captureRow.Children.Add(_status);

        var top = new StackPanel();
        top.Children.Add(capturePanel);
        top.Children.Add(captureRow);
        var topBorder = new Border { Child = top, BorderThickness = new Thickness(0, 0, 0, 1) };
        topBorder.Bind(Border.BackgroundProperty, topBorder.GetResourceObservable("Cc.PageHeader"));
        topBorder.Bind(Border.BorderBrushProperty, topBorder.GetResourceObservable("Cc.Rule"));

        var listTools = Ui.Row(_compare, remove);
        listTools.Margin = new Thickness(10, 10, 10, 8);
        var left = new DockPanel();
        DockPanel.SetDock(listTools, Dock.Top);
        left.Children.Add(listTools);
        _snapshots.Margin = new Thickness(6, 0, 6, 8);
        left.Children.Add(_snapshots);

        _details.Columns.Add(Ui.Column("Status", nameof(DisplayRow.Status), 90));
        _details.Columns.Add(Ui.Column("Work", nameof(DisplayRow.Work), 230));
        _details.Columns.Add(Ui.Column("Edition / CTS URN", nameof(DisplayRow.Edition), 300));
        _details.Columns.Add(Ui.Column("Frozen state / comparison", nameof(DisplayRow.Details)));
        _details.Margin = new Thickness(10);
        TableExport.Attach(_details, () => Ui.SafeName(project.Name, "project") + "-corpus-snapshot", DetailRows);

        var dock = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        dock.Children.Add(topBorder);
        dock.Children.Add(Ui.Split(Ui.Sidebar(left, double.NaN), _details, 350, 250, 400));
        Content = dock;

        Opened += (_, _) =>
        {
            _name.Text = $"Corpus state {DateTime.Now:yyyy-MM-dd HHmm}";
            Run(() => ReloadAsync());
        };
        Closing += (_, e) =>
        {
            if (_operation == null) return;
            _operation.Cancel();
            e.Cancel = true;
            _status.Text = "Cancelling the corpus operation…";
        };
    }

    private ResearchCorpusSnapshot? Current => _snapshots.SelectedItem as ResearchCorpusSnapshot;

    internal IReadOnlyList<ResearchCorpusSnapshot> Snapshots => _snapshots.ItemsSource as IReadOnlyList<ResearchCorpusSnapshot> ?? Array.Empty<ResearchCorpusSnapshot>();
    internal string StatusText => _status.Text ?? string.Empty;
    internal bool IsWorking => _operation != null;

    private async Task ReloadAsync(long select = 0)
    {
        var items = await _repo.GetSnapshotsAsync(_project.ResearchProjectId);
        _snapshots.ItemsSource = items;
        if (select > 0) _snapshots.SelectedItem = items.FirstOrDefault(x => x.ResearchCorpusSnapshotId == select);
        if (items.Count == 0)
        {
            _details.ItemsSource = null;
            _status.Text = "No frozen corpus state yet.";
        }
    }

    internal async Task CaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            await Alert.Info(this, "Corpus snapshot", "Enter a snapshot name.");
            return;
        }

        var scope = Ui.Value<CorpusSnapshotScope>(_scope);
        if (scope == CorpusSnapshotScope.EntireCorpus &&
            !await ConfirmAsync("Capture entire corpus", "Hash every installed edition? This can take time on a large corpus.", "Capture"))
            return;

        await RunOperationAsync(async token =>
        {
            var progress = new Progress<CorpusSnapshotProgress>(p =>
            {
                // A progress report can arrive after the summary has been written;
                // only a running operation may change the status line.
                if (_operation != null)
                    _status.Text = p.Total == 0 ? "Reading corpus…" : $"Fingerprinting {p.Completed:N0}/{p.Total:N0}: {p.CurrentWork}";
            });

            // On a worker thread. SQLite's async calls complete synchronously, so awaited
            // here the whole fingerprinting ran on the UI thread: the window froze,
            // Cancel could not be pressed, and every queued progress line ran after the
            // summary and overwrote it. The control values are read here, not there.
            var (name, notes, version) = (_name.Text!, _notes.Text ?? string.Empty, AppVersion());
            var snapshot = await Task.Run(() => _repo.CaptureAsync(_project.ResearchProjectId, name, scope, version, notes, progress, token), token);
            return async () =>
            {
                await ReloadAsync(snapshot.ResearchCorpusSnapshotId);
                _name.Text = $"Corpus state {DateTime.Now:yyyy-MM-dd HHmm}";
                _notes.Text = string.Empty;
                _status.Text = $"Captured {snapshot.WorkCount} works, {snapshot.EditionCount} editions, and {snapshot.TextNodeCount:N0} ordered text nodes.";
            };
        });
    }

    private async Task CompareAsync()
    {
        var snapshot = Current;
        if (snapshot == null) return;

        await RunOperationAsync(async token =>
        {
            var progress = new Progress<CorpusSnapshotProgress>(p =>
            {
                if (_operation != null)
                    _status.Text = p.Total == 0 ? "Reading corpus…" : $"Comparing {p.Completed:N0}/{p.Total:N0}: {p.CurrentWork}";
            });
            var result = await Task.Run(() => _repo.CompareAsync(snapshot, progress, token), token);
            return () =>
            {
                _details.ItemsSource = result.Differences.Select(d => new DisplayRow(d.Status, d.Work, d.Edition, d.Details)).ToList();
                _status.Text = result.Differences.Count == 0
                    ? $"Exact match: {result.Unchanged} frozen entries are unchanged."
                    : $"Compared: {result.Unchanged} unchanged, {result.Changed} changed, {result.Added} added, {result.Missing} missing.";
                return Task.CompletedTask;
            };
        });
    }

    private async Task ShowSnapshotAsync()
    {
        var snapshot = Current;
        if (snapshot == null) return;

        var entries = await _repo.GetEntriesAsync(snapshot.ResearchCorpusSnapshotId);
        _details.ItemsSource = entries.Select(e => new DisplayRow(
            e.AttributionStatus,
            e.AuthorName + " — " + e.WorkTitle,
            e.EditionCtsUrn ?? "(no edition)",
            e.EditionCtsUrn == null
                ? "No installed edition"
                : $"{e.EditionKind}; {e.Language ?? "unknown language"}; {e.TextNodeCount:N0} nodes; SHA-256 {e.ContentSha256?[..Math.Min(16, e.ContentSha256.Length)]}…")).ToList();
        if (_operation == null)
            _status.Text = $"{snapshot.Name}: {Words.Of(snapshot.Scope)}; {snapshot.WorkCount} works; {snapshot.EditionCount} editions; app {snapshot.AppVersion}; captured {snapshot.CreatedUtc.ToLocalTime():g}.";
    }

    private async Task RemoveAsync()
    {
        var snapshot = Current;
        if (snapshot == null) return;
        if (!await ConfirmAsync("Remove snapshot", $"Remove the frozen corpus snapshot '{snapshot.Name}'?", "Remove", AlertKind.Warning)) return;

        await _repo.DeleteAsync(snapshot.ResearchCorpusSnapshotId);
        await ReloadAsync();
    }

    /// <summary>
    /// Runs a capture or comparison with Cancel live, then applies its result
    /// once the operation is over - so the last progress line cannot land on top
    /// of the summary.
    /// </summary>
    private async Task RunOperationAsync(Func<CancellationToken, Task<Func<Task>>> action)
    {
        _operation = new CancellationTokenSource();
        _capture.IsEnabled = false;
        _compare.IsEnabled = false;
        _cancel.IsEnabled = true;
        Func<Task>? finish = null;
        try
        {
            finish = await action(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Operation cancelled; no partial snapshot was saved.";
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Corpus snapshot", ex.Message);
            _status.Text = "The operation did not complete.";
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _capture.IsEnabled = true;
            _compare.IsEnabled = true;
            _cancel.IsEnabled = false;
        }

        if (finish != null) await finish();
    }

    private IReadOnlyList<IReadOnlyList<string>> DetailRows()
    {
        var rows = new List<IReadOnlyList<string>> { new[] { "Status", "Work", "Edition / CTS URN", "Frozen state / comparison" } };
        if (_details.ItemsSource is IEnumerable<DisplayRow> shown)
            rows.AddRange(shown.Select(r => (IReadOnlyList<string>)new[] { r.Status, r.Work, r.Edition, r.Details }));
        return rows;
    }

    /// <summary>The version recorded with a snapshot - the app's own, as Windows records ProductVersion.</summary>
    private static string AppVersion()
    {
        var version = typeof(CorpusSnapshotsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? typeof(CorpusSnapshotsWindow).Assembly.GetName().Version?.ToString()
                      ?? "unknown";
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    private sealed record DisplayRow(string Status, string Work, string Edition, string Details);
}
