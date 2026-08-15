using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Human-previewed import of offline RIS and BibTeX exports.</summary>
public sealed class BibliographyImportForm : Form
{
    private readonly ResearchProject _project;
    private readonly ResearchRepository _repo = new();
    private readonly DataGridView _records = new();
    private readonly ComboBox _question = new();
    private readonly TextBox _details = new();
    private readonly Label _status = new();
    private readonly Button _import = new();
    private List<EvidenceItem> _existing = new();

    public int ImportedCount { get; private set; }

    public BibliographyImportForm(ResearchProject project)
    {
        _project = project;
        Text = $"Import Bibliography — {project.Name}";
        Width = 1120;
        Height = 720;
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "WordStudy");

        var header = new Panel { Dock = DockStyle.Top, Height = 86, Padding = new Padding(10) };
        var choose = new Button { Text = "Choose RIS or BibTeX…", Left = 10, Top = 10, Width = 175, Height = 30 };
        choose.Click += async (_, _) => await ChooseFileAsync();
        var questionLabel = new Label { Text = "Link imported sources to:", Left = 205, Top = 17, Width = 145 };
        _question.SetBounds(350, 12, 430, 26);
        _question.DropDownStyle = ComboBoxStyle.DropDownList;
        var explanation = new Label
        {
            Text = "Preview first. Selected records become uncertain scholarship evidence; abstracts remain raw source summaries, not findings.",
            Left = 10, Top = 52, Width = 1040, Height = 22,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        header.Controls.AddRange(new Control[] { choose, questionLabel, _question, explanation });

        _records.Dock = DockStyle.Fill;
        _records.AutoGenerateColumns = false;
        _records.AllowUserToAddRows = false;
        _records.AllowUserToDeleteRows = false;
        _records.MultiSelect = false;
        _records.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _records.RowHeadersVisible = false;
        _records.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = "Include", HeaderText = "Import", Width = 58
        });
        _records.Columns.Add(Column("Status", "Status", 82));
        _records.Columns.Add(Column("Format", "Format", 68));
        _records.Columns.Add(Column("Authors", "Author(s)", 210));
        _records.Columns.Add(Column("Year", "Year", 62));
        _records.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Title", HeaderText = "Title",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true
        });
        _records.Columns.Add(Column("Identifier", "DOI / identifier", 220));
        foreach (DataGridViewColumn column in _records.Columns)
            if (column is not DataGridViewCheckBoxColumn) column.ReadOnly = true;
        _records.SelectionChanged += (_, _) => ShowDetails();
        _records.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_records.IsCurrentCellDirty) _records.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _records.CellBeginEdit += (_, e) =>
        {
            if (e.ColumnIndex == 0 && _records.Rows[e.RowIndex].DataBoundItem is ImportRow row &&
                (row.IsDuplicate || row.Imported)) e.Cancel = true;
        };
        _records.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex == 0 && _records.DataSource is IEnumerable<ImportRow> rows)
                _import.Enabled = rows.Any(r => r.Include && !r.IsDuplicate && !r.Imported);
        };

        _details.Dock = DockStyle.Bottom;
        _details.Height = 128;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Vertical;

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10) };
        _import.Text = "Import selected";
        _import.SetBounds(10, 10, 120, 30);
        _import.Enabled = false;
        _import.Click += async (_, _) => await ImportSelectedAsync();
        var close = new Button { Text = "Close", Width = 90, Height = 30, Top = 10 };
        close.Left = footer.ClientSize.Width - close.Width - 10;
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Click += (_, _) => Close();
        _status.SetBounds(145, 16, 790, 22);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        footer.Controls.AddRange(new Control[] { _import, close, _status });

        Controls.Add(_records);
        Controls.Add(_details);
        Controls.Add(footer);
        Controls.Add(header);
        ReadingTheme.AttachTo(this, () =>
        {
            explanation.ForeColor = ReadingTheme.MutedText;
            _status.ForeColor = ReadingTheme.MutedText;
        });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) => await LoadContextAsync();
    }

    private async Task LoadContextAsync()
    {
        _existing = await _repo.GetEvidenceAsync(_project.ResearchProjectId);
        var questions = await _repo.GetQuestionsAsync(_project.ResearchProjectId);
        var choices = new List<QuestionChoice> { new(null, "General project evidence") };
        choices.AddRange(questions.Select(q => new QuestionChoice(q.ResearchQuestionId, q.Text)));
        _question.DataSource = choices;
        _status.Text = "Choose an exported .ris or .bib file. No network lookup is performed.";
    }

    private async Task ChooseFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import bibliography",
            Filter = "RIS or BibTeX (*.ris;*.bib)|*.ris;*.bib|RIS (*.ris)|*.ris|BibTeX (*.bib)|*.bib|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var text = await File.ReadAllTextAsync(dialog.FileName);
            var parsed = BibliographyImport.Parse(text, dialog.FileName);
            var existingKeys = _existing.SelectMany(EvidenceKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ImportRow>();
            foreach (var record in parsed)
            {
                var keys = RecordKeys(record).ToList();
                var duplicate = keys.Any(existingKeys.Contains) || keys.Any(k => !fileKeys.Add(k));
                foreach (var key in keys) fileKeys.Add(key);
                rows.Add(new ImportRow(record, !duplicate, duplicate));
            }
            _records.DataSource = rows;
            _import.Enabled = rows.Any(r => r.Include);
            _status.Text = parsed.Count == 0
                ? "No recognizable bibliography records were found."
                : $"Parsed {parsed.Count} record(s); {rows.Count(r => r.IsDuplicate)} duplicate(s) left unselected.";
            ShowDetails();
        }
        catch (Exception ex)
        {
            _records.DataSource = null;
            _import.Enabled = false;
            _status.Text = $"Could not read that bibliography: {ex.Message}";
        }
    }

    private async Task ImportSelectedAsync()
    {
        _records.EndEdit();
        var rows = (_records.DataSource as IEnumerable<ImportRow>)?
                       .Where(r => r.Include && !r.IsDuplicate && !r.Imported).ToList()
                   ?? new List<ImportRow>();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Select at least one new bibliography record.");
            return;
        }

        var questionId = (_question.SelectedItem as QuestionChoice)?.Id;
        var added = 0;
        foreach (var row in rows)
        {
            var record = row.Record;
            var citation = record.FormatCitation();
            var notes = record.Keywords.Count == 0 ? null : "Imported keywords: " + string.Join(", ", record.Keywords);
            await _repo.SaveEvidenceAsync(new EvidenceItem
            {
                ResearchProjectId = _project.ResearchProjectId,
                ResearchQuestionId = questionId,
                Title = record.DisplayTitle,
                Type = EvidenceType.Scholarship,
                SourceType = $"{record.ImportFormat} bibliography ({record.EntryType})",
                StableIdentifier = Empty(record.StableIdentifier),
                Provenance = citation + (string.IsNullOrWhiteSpace(record.CiteKey)
                    ? $" Imported from {record.ImportFormat} metadata."
                    : $" Imported from {record.ImportFormat} metadata; cite key {record.CiteKey}."),
                Excerpt = Empty(record.Abstract),
                ResearcherNote = notes,
                Judgment = EvidenceJudgment.Uncertain,
                Relationship = EvidenceRelationship.Contextualizes,
                Origin = EvidenceOrigin.Manual,
                SortOrder = _existing.Count + added
            });
            row.Include = false;
            row.Imported = true;
            added++;
        }
        ImportedCount += added;
        _existing = await _repo.GetEvidenceAsync(_project.ResearchProjectId);
        _records.Refresh();
        _import.Enabled = false;
        _status.Text = $"Imported {added} scholarship source(s) as uncertain evidence for human review.";
    }

    private void ShowDetails()
    {
        if (_records.CurrentRow?.DataBoundItem is not ImportRow row)
        {
            _details.Clear();
            return;
        }
        _details.Text = row.Record.FormatCitation() + Environment.NewLine + Environment.NewLine +
                        (string.IsNullOrWhiteSpace(row.Record.Abstract)
                            ? "No abstract was present in the export."
                            : row.Record.Abstract);
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
    private static DataGridViewTextBoxColumn Column(string property, string header, int width) =>
        new() { DataPropertyName = property, HeaderText = header, Width = width };
    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record QuestionChoice(long? Id, string Text) { public override string ToString() => Text; }
    private sealed class ImportRow
    {
        public ImportRow(BibliographyRecord record, bool include, bool duplicate)
        {
            Record = record; Include = include; IsDuplicate = duplicate;
        }
        public BibliographyRecord Record { get; }
        public bool Include { get; set; }
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
