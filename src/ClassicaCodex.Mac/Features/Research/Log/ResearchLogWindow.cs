using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Log;

/// <summary>Chronological, append-only history for one research project.</summary>
internal sealed class ResearchLogWindow : ResearchWindow
{
    private readonly ResearchProject _project;
    private readonly ResearchRepository _repo = new();
    private readonly DataGrid _entries = Ui.Table();
    private readonly TextBox _summary = Ui.Line("Decision, observation, next step, or unresolved concern");
    private readonly TextBox _details = Ui.Area(52, "Optional context or rationale");
    private readonly TextBlock _status = Ui.Status();
    private List<ResearchLogEntry> _items = new();

    public ResearchLogWindow(ResearchProject project) : base(null)
    {
        _project = project;
        Title = $"Research Log — {project.Name}";
        Width = 1000;
        Height = 680;
        MinWidth = 760;
        MinHeight = 520;

        var header = Ui.Header("Project history — newest first", "Every change the Bench and its windows make is recorded here, beside the notes you add. Entries are never edited or removed.");

        _entries.Columns.Add(Ui.Column("When", nameof(EntryRow.When), 150));
        _entries.Columns.Add(Ui.Column("Kind", nameof(EntryRow.Kind), 196));
        _entries.Columns.Add(Ui.Column("Entry", nameof(EntryRow.Summary)));
        _entries.Columns.Add(Ui.Column("Details", nameof(EntryRow.Details), 260));
        _entries.Margin = new Thickness(16, 12, 16, 8);
        TableExport.Attach(_entries, () => Ui.SafeName(project.Name, "project") + "-research-log", Rows);

        var add = Ui.Button("Add Note", "Plus", accent: true, tip: "Add the note to the log (" + Ui.Chord("↩") + ")");
        add.Click += (_, _) => Run(AddNoteAsync);
        var summaryRow = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        add.Margin = new Thickness(10, 0, 0, 0);
        summaryRow.Children.Add(add);
        summaryRow.Children.Add(_summary);

        var addPanel = new StackPanel { Margin = new Thickness(16, 10, 16, 12), Spacing = 8 };
        addPanel.Children.Add(Ui.FieldLabel("Add a dated research note"));
        addPanel.Children.Add(summaryRow);
        addPanel.Children.Add(_details);
        var addBorder = new Border { Child = addPanel, BorderThickness = new Thickness(0, 1, 0, 0) };
        addBorder.Bind(Border.BorderBrushProperty, addBorder.GetResourceObservable("Cc.Rule"));

        var status = Ui.StatusBar(_status);
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(addBorder, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(status);
        dock.Children.Add(addBorder);
        dock.Children.Add(_entries);
        Content = dock;

        Opened += (_, _) => Run(ReloadAsync);
    }

    internal IReadOnlyList<ResearchLogEntry> Entries => _items;

    protected override bool OnCommandKey(Key key, bool shift)
    {
        if (key != Key.Enter) return false;
        Run(AddNoteAsync);
        return true;
    }

    internal async Task AddNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(_summary.Text))
        {
            await Alert.Info(this, "Research log", "Enter a research note.");
            return;
        }

        await _repo.AddResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = _project.ResearchProjectId,
            Summary = _summary.Text.Trim(),
            Details = Ui.Clean(_details.Text)
        });
        _summary.Text = string.Empty;
        _details.Text = string.Empty;
        await ReloadAsync();
        _status.Text = "Research note added. Log entries are retained with the project.";
        _summary.Focus();
    }

    internal void Draft(string summary, string? details = null)
    {
        _summary.Text = summary;
        _details.Text = details ?? string.Empty;
    }

    internal async Task ReloadAsync()
    {
        _items = await _repo.GetResearchLogAsync(_project.ResearchProjectId);
        _entries.ItemsSource = _items.Select(e => new EntryRow(e)).ToList();
        _status.Text = $"{_items.Count} log entr{(_items.Count == 1 ? "y" : "ies")}.";
    }

    private IReadOnlyList<IReadOnlyList<string>> Rows()
    {
        var rows = new List<IReadOnlyList<string>> { new[] { "When", "Kind", "Entry", "Details" } };
        rows.AddRange(_items.Select(e => (IReadOnlyList<string>)new[] { e.CreatedUtc.ToLocalTime().ToString("g"), e.Kind.ToString(), e.Summary, e.Details ?? string.Empty }));
        return rows;
    }

    private sealed class EntryRow
    {
        public EntryRow(ResearchLogEntry entry) => Entry = entry;

        public ResearchLogEntry Entry { get; }
        public string When => Entry.CreatedUtc.ToLocalTime().ToString("g");
        public string Kind => Words.Of(Entry.Kind);
        public string Summary => Entry.Summary;
        public string Details => Entry.Details ?? string.Empty;
    }
}
