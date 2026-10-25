using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Bibliography;

/// <summary>Human-previewed import of offline RIS and BibTeX exports.</summary>
internal sealed class BibliographyImportWindow : ResearchWindow
{
    private static readonly FilePickerFileType Either = new("RIS or BibTeX") { Patterns = new[] { "*.ris", "*.bib" } };
    private static readonly FilePickerFileType AnyFile = new("All files") { Patterns = new[] { "*" } };

    private readonly ResearchProject _project;
    private readonly ResearchRepository _repo = new();
    private readonly ResearchBibliographyRepository _bibliography = new();
    private readonly CheckTable<ImportRow> _records;
    private readonly ComboBox _question = Ui.Combo(Ui.QuestionChoices("General project evidence", Array.Empty<ResearchQuestion>()), 360);
    private readonly TextBox _details = Ui.Area(128, readOnly: true);
    private readonly TextBlock _status = Ui.Status();
    private readonly Button _import = Ui.Button("Import Selected", "Download", accent: true);
    private List<EvidenceItem> _existing = new();

    public BibliographyImportWindow(ResearchProject project) : base(null)
    {
        _project = project;
        Title = $"Import Bibliography — {project.Name}";
        Width = 1120;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;

        var choose = Ui.Button("Choose RIS or BibTeX…", "Folder");
        choose.Click += (_, _) => Run(ChooseFileAsync);
        var link = new TextBlock { Text = "Link imported sources to:", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
        var row = Ui.Row(choose, link, _question);

        var top = new StackPanel { Margin = new Thickness(16, 14, 16, 10), Spacing = 10 };
        top.Children.Add(row);
        top.Children.Add(Ui.Muted("Preview first. Selected records become uncertain scholarship evidence; abstracts remain raw source summaries, not findings."));

        _records = new CheckTable<ImportRow>("Import",
            new CheckColumn<ImportRow>("Status", new GridLength(82), r => r.Status),
            new CheckColumn<ImportRow>("Format", new GridLength(68), r => r.Format),
            new CheckColumn<ImportRow>("Author(s)", new GridLength(170), r => r.Authors, Wrap: true),
            new CheckColumn<ImportRow>("Year", new GridLength(62), r => r.Year),
            new CheckColumn<ImportRow>("Title", new GridLength(1, GridUnitType.Star), r => r.Title, Wrap: true),
            new CheckColumn<ImportRow>("DOI / identifier", new GridLength(190), r => r.Identifier));
        _records.Margin = new Thickness(16, 0, 16, 10);
        _records.SelectionChanged += ShowDetails;
        _records.CheckedChanged += () => _import.IsEnabled = _records.Checked.Count > 0;
        _details.Margin = new Thickness(16, 0, 16, 10);

        _import.IsEnabled = false;
        _import.Click += (_, _) => Run(ImportSelectedAsync);
        var close = Ui.Button("Close");
        close.IsCancel = true;
        close.Click += (_, _) => Close();

        var bar = Ui.ButtonBar(_status, close, _import);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        DockPanel.SetDock(_details, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bar);
        dock.Children.Add(_details);
        dock.Children.Add(_records);
        Content = dock;

        Opened += (_, _) => Run(LoadContextAsync);
    }

    /// <summary>How many sources were imported while the window was open.</summary>
    public int ImportedCount { get; private set; }

    internal CheckTable<ImportRow> Records => _records;

    private async Task LoadContextAsync()
    {
        _existing = await _repo.GetEvidenceAsync(_project.ResearchProjectId);
        var questions = await _repo.GetQuestionsAsync(_project.ResearchProjectId);
        Ui.SetChoices(_question, Ui.QuestionChoices("General project evidence", questions), Ui.Value<long?>(_question));
        _status.Text = "Choose an exported .ris or .bib file. No network lookup is performed.";
    }

    private async Task ChooseFileAsync()
    {
        var path = await Files.OpenAsync(this, "Import bibliography", Either, ResearchBibliographyWindow.Ris, ResearchBibliographyWindow.BibTeX, AnyFile);
        if (path == null) return;
        await LoadFileAsync(path);
    }

    /// <summary>Parses a file into the preview, marking duplicates of the project and of each other.</summary>
    internal async Task LoadFileAsync(string path)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path);
            var parsed = BibliographyImport.Parse(text, path);
            var existingKeys = _existing.SelectMany(EvidenceKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ImportRow>();
            foreach (var record in parsed)
            {
                var keys = RecordKeys(record).ToList();
                var duplicate = keys.Any(existingKeys.Contains) || keys.Any(k => !fileKeys.Add(k));
                foreach (var key in keys) fileKeys.Add(key);
                rows.Add(new ImportRow(record, duplicate));
            }

            _records.SetItems(rows, initiallyChecked: r => !r.IsDuplicate, canCheck: r => !r.IsDuplicate && !r.Imported);
            _import.IsEnabled = _records.Checked.Count > 0;
            _status.Text = parsed.Count == 0
                ? "No recognizable bibliography records were found."
                : $"Parsed {parsed.Count} record(s); {rows.Count(r => r.IsDuplicate)} duplicate(s) left unselected.";
            ShowDetails();
        }
        catch (Exception ex)
        {
            _records.SetItems(Array.Empty<ImportRow>());
            _import.IsEnabled = false;
            _status.Text = $"Could not read that bibliography: {ex.Message}";
        }
    }

    internal async Task ImportSelectedAsync()
    {
        var rows = _records.Checked.Where(r => !r.IsDuplicate && !r.Imported).ToList();
        if (rows.Count == 0)
        {
            await Alert.Info(this, "Import bibliography", "Select at least one new bibliography record.");
            return;
        }

        var questionId = Ui.Value<long?>(_question);
        var added = 0;
        foreach (var row in rows)
        {
            var record = row.Record;
            var citation = record.FormatCitation();
            var notes = record.Keywords.Count == 0 ? null : "Imported keywords: " + string.Join(", ", record.Keywords);
            var evidence = new EvidenceItem
            {
                ResearchProjectId = _project.ResearchProjectId,
                ResearchQuestionId = questionId,
                Title = record.DisplayTitle,
                Type = EvidenceType.Scholarship,
                SourceType = $"{record.ImportFormat} bibliography ({record.EntryType})",
                StableIdentifier = Ui.Clean(record.StableIdentifier),
                Provenance = citation + (string.IsNullOrWhiteSpace(record.CiteKey)
                    ? $" Imported from {record.ImportFormat} metadata."
                    : $" Imported from {record.ImportFormat} metadata; cite key {record.CiteKey}."),
                Excerpt = Ui.Clean(record.Abstract),
                ResearcherNote = notes,
                Judgment = EvidenceJudgment.Uncertain,
                Relationship = EvidenceRelationship.Contextualizes,
                Origin = EvidenceOrigin.Manual,
                SortOrder = _existing.Count + added
            };
            await _repo.SaveEvidenceAsync(evidence);
            await _bibliography.SaveAsync(EvidenceBibliographyMetadata.FromRecord(evidence.EvidenceItemId, record));
            row.Imported = true;
            added++;
        }

        ImportedCount += added;
        _existing = await _repo.GetEvidenceAsync(_project.ResearchProjectId);
        _records.Refresh();
        _import.IsEnabled = false;
        _status.Text = $"Imported {added} scholarship source(s) as uncertain evidence for human review.";
    }

    private void ShowDetails()
    {
        if (_records.SelectedItem is not { } row)
        {
            _details.Text = string.Empty;
            return;
        }

        _details.Text = row.Record.FormatCitation() + Environment.NewLine + Environment.NewLine +
                        (string.IsNullOrWhiteSpace(row.Record.Abstract) ? "No abstract was present in the export." : row.Record.Abstract);
    }

    private static IEnumerable<string> EvidenceKeys(EvidenceItem evidence)
    {
        if (IdentifierKey(evidence.StableIdentifier) is { } identifier)
        {
            yield return identifier;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(evidence.Title)) yield return "title:" + NormalizeKey(evidence.Title);
    }

    private static IEnumerable<string> RecordKeys(BibliographyRecord record)
    {
        if (IdentifierKey(record.StableIdentifier) is { } identifier)
        {
            yield return identifier;
            yield break;
        }

        yield return "title:" + NormalizeKey(record.DisplayTitle);
    }

    private static string? IdentifierKey(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        var doi = BibliographyImport.NormalizeDoi(identifier);
        if (doi != null && doi.StartsWith("10.", StringComparison.Ordinal)) return "doi:" + doi;
        return "id:" + identifier.Trim().ToLowerInvariant();
    }

    private static string NormalizeKey(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal sealed class ImportRow
    {
        public ImportRow(BibliographyRecord record, bool duplicate)
        {
            Record = record;
            IsDuplicate = duplicate;
        }

        public BibliographyRecord Record { get; }
        public bool IsDuplicate { get; }
        public bool Imported { get; set; }
        public string Status => Imported ? "Imported" : IsDuplicate ? "Duplicate" : "New";
        public string Format => Record.ImportFormat;
        public string Authors => string.Join("; ", Record.Authors);
        public string Year => Record.Year ?? "—";
        public string Title => Record.Title;
        public string Identifier => string.IsNullOrWhiteSpace(Record.StableIdentifier) ? "—" : Record.StableIdentifier;
    }
}
