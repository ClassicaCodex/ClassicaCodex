using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Bibliography;

/// <summary>Project bibliography, citekey editor, and offline Zotero-compatible export.</summary>
internal sealed partial class ResearchBibliographyWindow : ResearchWindow
{
    internal static readonly FilePickerFileType BibTeX = new("BibTeX") { Patterns = new[] { "*.bib" } };
    internal static readonly FilePickerFileType Ris = new("RIS") { Patterns = new[] { "*.ris" } };

    private const string Columns = "64,170,220,64,*,90";

    private readonly ResearchProject _project;
    private readonly ResearchBibliographyRepository _repo = new();
    private readonly ListBox _list;
    private readonly TextBox _preview;
    private readonly TextBlock _status = Ui.Status();
    private List<Row> _rows = new();

    public ResearchBibliographyWindow(ResearchProject project) : base(null)
    {
        _project = project;
        Title = $"Bibliography & Zotero Export — {project.Name}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;

        var save = Ui.Button("Save Citekeys", "Check", tip: "Save the citekeys and citation records (" + Ui.Chord("S") + ")");
        var bib = Ui.Button("Export BibTeX…", "Export");
        var ris = Ui.Button("Export RIS…", "Export");
        save.Click += (_, _) => Run(() => SaveKeysAsync(showConfirmation: true));
        bib.Click += (_, _) => Run(() => ExportAsync("BibTeX"));
        ris.Click += (_, _) => Run(() => ExportAsync("RIS"));

        var header = Ui.Header("Bibliography",
            "Scholarship evidence retains structured citation metadata. Edit citekeys, select records, then export for Zotero or another reference manager.");
        var actions = new DockPanel { Margin = new Thickness(16, 10, 16, 8) };
        var buttons = Ui.Row(save, bib, ris);
        DockPanel.SetDock(buttons, Dock.Left);
        _status.Margin = new Thickness(14, 0, 0, 0);
        actions.Children.Add(buttons);
        actions.Children.Add(_status);

        var columnHeader = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(10, 7) };
        foreach (var (text, column) in new[] { ("Export", 0), ("Citekey", 1), ("Author(s)", 2), ("Year", 3), ("Title", 4), ("Type", 5) })
        {
            var label = new TextBlock { Text = text, Classes = { "caption" }, FontWeight = FontWeight.SemiBold };
            Grid.SetColumn(label, column);
            columnHeader.Children.Add(label);
        }

        var headerBorder = new Border { Child = columnHeader, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Cc.PageHeader"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Cc.Rule"));

        _list = Ui.Compact(new ListBox { ItemTemplate = new FuncDataTemplate<Row>((row, _) => row == null ? new TextBlock() : BuildRow(row), supportsRecycling: false) }, 4);
        _list.SelectionChanged += (_, _) => ShowPreview();

        var tableDock = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        tableDock.Children.Add(headerBorder);
        tableDock.Children.Add(_list);
        var table = new Border { Child = tableDock, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(16, 0, 16, 10) };
        table.Bind(Border.BorderBrushProperty, table.GetResourceObservable("Cc.Rule"));
        table.Bind(Border.BackgroundProperty, table.GetResourceObservable("Cc.Page"));

        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 12,
            Height = 180,
            Margin = new Thickness(16, 0, 16, 14),
            VerticalContentAlignment = VerticalAlignment.Top
        };
        _preview.Bind(TextBox.FontFamilyProperty, _preview.GetResourceObservable("Cc.MonoFont"));

        var previewPanel = new StackPanel();
        var previewLabel = Ui.Section("BibTeX preview of the selected record");
        previewLabel.Margin = new Thickness(16, 0, 16, 6);
        previewPanel.Children.Add(previewLabel);
        previewPanel.Children.Add(_preview);

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(actions, Dock.Top);
        DockPanel.SetDock(previewPanel, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(actions);
        dock.Children.Add(previewPanel);
        dock.Children.Add(table);
        Content = dock;

        Opened += (_, _) => Run(LoadAsync);
    }

    protected override bool OnCommandKey(Key key, bool shift)
    {
        if (key != Key.S) return false;
        Run(() => SaveKeysAsync(showConfirmation: true));
        return true;
    }

    internal async Task LoadAsync()
    {
        var metadata = await _repo.GetForProjectAsync(_project.ResearchProjectId);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _rows = metadata.Select(m =>
        {
            var baseKey = string.IsNullOrWhiteSpace(m.CiteKey) ? BibliographyExport.SuggestCiteKey(m.ToRecord()) : m.CiteKey!;
            var key = baseKey;
            for (var suffix = 2; !used.Add(key); suffix++) key = baseKey + suffix;
            m.CiteKey = key;
            return new Row(m);
        }).ToList();
        _list.ItemsSource = _rows;
        if (_rows.Count > 0) _list.SelectedIndex = 0;
        _status.Text = _rows.Count == 0
            ? "No scholarship evidence is available to export."
            : $"{_rows.Count} scholarship source(s); generated citekeys remain editable.";
        ShowPreview();
    }

    internal async Task<bool> SaveKeysAsync(bool showConfirmation)
    {
        var invalid = _rows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.CiteKey) || !CiteKeyPattern().IsMatch(r.CiteKey));
        if (invalid != null)
        {
            await Alert.Warn(this, "Invalid citekey", $"The citekey for '{invalid.Title}' contains unsupported characters.");
            return false;
        }

        var duplicate = _rows.GroupBy(r => r.CiteKey, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            await Alert.Warn(this, "Duplicate citekey", $"The citekey '{duplicate.Key}' is used more than once.");
            return false;
        }

        foreach (var row in _rows)
        {
            row.Metadata.CiteKey = row.CiteKey.Trim();
            await _repo.SaveAsync(row.Metadata);
        }

        if (showConfirmation) _status.Text = $"Saved {_rows.Count} citekey(s) and structured citation record(s).";
        return true;
    }

    private async Task ExportAsync(string format)
    {
        if (!await SaveKeysAsync(showConfirmation: false)) return;

        var selected = _rows.Where(r => r.Include).Select(r => r.Metadata.ToRecord()).ToList();
        if (selected.Count == 0)
        {
            await Alert.Info(this, "Export bibliography", "Select at least one bibliography record to export.");
            return;
        }

        var bib = format == "BibTeX";
        var path = await Files.SaveAsync(this, $"Export {format} bibliography",
            Ui.SafeName(_project.Name, "bibliography") + (bib ? ".bib" : ".ris"), bib ? BibTeX : Ris);
        if (path == null) return;

        var text = bib ? BibliographyExport.ToBibTeX(selected) : BibliographyExport.ToRis(selected);

        // Say so rather than fall over: a read-only folder, a full disk, a .bib
        // already open in a reference manager. The log entry is only written once
        // the file actually exists, so the project's history does not record an
        // export that never happened.
        try
        {
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Export failed", $"Could not write the file.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return;
        }

        await new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchLogEntryKind.BibliographyExported,
            Summary = $"Exported {selected.Count} source(s) as {format}",
            Details = Path.GetFileName(path)
        });
        _status.Text = $"Exported {selected.Count} source(s) to {Path.GetFileName(path)}.";
    }

    private void ShowPreview()
    {
        if (_list.SelectedItem is not Row row)
        {
            _preview.Text = string.Empty;
            return;
        }

        row.Metadata.CiteKey = row.CiteKey;
        _preview.Text = BibliographyExport.ToBibTeX(new[] { row.Metadata.ToRecord() });
    }

    private Control BuildRow(Row row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns) };
        var include = new CheckBox { IsChecked = row.Include, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        include.IsCheckedChanged += (_, _) => row.Include = include.IsChecked == true;
        var key = new TextBox { Text = row.CiteKey, FontSize = 12, Margin = new Thickness(0, 0, 10, 0) };
        key.Bind(TextBox.FontFamilyProperty, key.GetResourceObservable("Cc.MonoFont"));
        key.TextChanged += (_, _) =>
        {
            row.CiteKey = key.Text ?? string.Empty;
            if (ReferenceEquals(_list.SelectedItem, row)) ShowPreview();
        };
        key.GotFocus += (_, _) => _list.SelectedItem = row;

        Grid.SetColumn(include, 0);
        Grid.SetColumn(key, 1);
        grid.Children.Add(include);
        grid.Children.Add(key);
        foreach (var (text, column) in new[] { (row.Authors, 2), (row.Year, 3), (row.Title, 4), (row.EntryType, 5) })
        {
            var cell = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        return grid;
    }

    internal IReadOnlyList<(string CiteKey, string Title)> Records => _rows.Select(r => (r.CiteKey, r.Title)).ToList();

    [GeneratedRegex(@"^[\p{L}\p{N}_:.+\-]+$")]
    private static partial Regex CiteKeyPattern();

    private sealed class Row
    {
        public Row(EvidenceBibliographyMetadata metadata)
        {
            Metadata = metadata;
            CiteKey = metadata.CiteKey ?? string.Empty;
        }

        public EvidenceBibliographyMetadata Metadata { get; }
        public bool Include { get; set; } = true;
        public string CiteKey { get; set; }
        public string Authors => Metadata.Authors.Count == 0 ? "—" : string.Join("; ", Metadata.Authors);
        public string Year => Metadata.Year ?? "—";
        public string Title => Metadata.Title;
        public string EntryType => Metadata.EntryType;
    }
}
