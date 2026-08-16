using System.Diagnostics;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>A staging area for sources and passages that have not yet become evidence.</summary>
public sealed class ResearchReadingQueueForm : Form
{
    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly ResearchReadingQueueRepository _queueRepo = new();
    private readonly ResearchRepository _researchRepo = new();
    private readonly DataGridView _items = new();
    private readonly TextBox _title = new();
    private readonly ComboBox _status = new();
    private readonly ComboBox _priority = new();
    private readonly ComboBox _question = new();
    private readonly TextBox _purpose = new();
    private readonly TextBox _identifier = new();
    private readonly TextBox _locator = new();
    private readonly TextBox _quotation = new() { Name = "SourceQuotationTextBox" };
    private readonly TextBox _notes = new() { Name = "ResearcherReadingNotesTextBox" };
    private readonly Label _kindLine = new();
    private readonly Label _promotionLine = new();
    private ResearchReadingItem? _editing;
    private List<EvidenceItem> _evidence = [];

    public (int WorkId, long TextNodeId)? NavigationTarget { get; private set; }
    public long? PromotedEvidenceItemId { get; private set; }

    public ResearchReadingQueueForm(ResearchProject project, Work work)
    {
        _project = project;
        _work = work;
        Text = $"Reading Queue — {project.Name}";
        AppIcons.ApplyWindowIcon(this, "WordStudy");
        Width = 1280;
        Height = 790;
        MinimumSize = new Size(1050, 650);
        StartPosition = FormStartPosition.CenterParent;

        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 9, 10, 4),
            Text = "Reading queue · passages and sources stay provisional until you review and promote them to evidence."
        };
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        BuildList(split.Panel1);
        BuildEditor(split.Panel2);
        Controls.Add(split);
        Controls.Add(notice);

        ReadingTheme.AttachTo(this, () => notice.ForeColor = ReadingTheme.MutedText);
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            var maximum = split.ClientSize.Width - 560 - split.SplitterWidth;
            if (maximum >= 380)
            {
                split.SplitterDistance = Math.Clamp(455, 380, maximum);
                split.Panel1MinSize = 380;
                split.Panel2MinSize = 560;
            }
            await LoadAsync();
        };
    }

    private void BuildList(Control host)
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(5) };
        var passage = ActionButton("Add passage…", 105);
        var source = ActionButton("Queue evidence…", 115);
        var external = ActionButton("New external", 100);
        var remove = ActionButton("Remove", 75);
        passage.Click += async (_, _) => await AddPassageAsync();
        source.Click += async (_, _) => await AddEvidenceSourceAsync();
        external.Click += (_, _) => NewExternal();
        remove.Click += async (_, _) => await RemoveAsync();
        actions.Controls.AddRange([passage, source, external, remove]);

        _items.Dock = DockStyle.Fill;
        _items.AutoGenerateColumns = false;
        _items.AllowUserToAddRows = false;
        _items.AllowUserToDeleteRows = false;
        _items.ReadOnly = true;
        _items.MultiSelect = false;
        _items.RowHeadersVisible = false;
        _items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _items.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResearchReadingItem.Priority), HeaderText = "Priority", Width = 65 });
        _items.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResearchReadingItem.Status), HeaderText = "Status", Width = 72 });
        _items.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResearchReadingItem.Title), HeaderText = "Reading", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _items.SelectionChanged += (_, _) => ShowItem(_items.CurrentRow?.DataBoundItem as ResearchReadingItem);
        host.Controls.Add(_items);
        host.Controls.Add(actions);
    }

    private void BuildEditor(Control host)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6) };
        var save = ActionButton("Save notes", 95);
        var open = ActionButton("Open source", 95);
        var promote = ActionButton("Promote to evidence", 145);
        save.Click += async (_, _) => await SaveAsync();
        open.Click += async (_, _) => await OpenSourceAsync();
        promote.Click += async (_, _) => await PromoteAsync();
        buttons.Controls.AddRange([save, open, promote]);

        var y = 10;
        _kindLine.SetBounds(10, y, 670, 22); y += 29;
        scroll.Controls.Add(_kindLine);
        AddField(scroll, "Title", _title, ref y);
        AddComboPair(scroll, "Status", _status, Enum.GetValues<ResearchReadingStatus>(), "Priority", _priority, Enum.GetValues<ResearchReadingPriority>(), ref y);
        AddCombo(scroll, "Research question", _question, ref y);
        AddArea(scroll, "Why read this?", _purpose, 65, ref y);
        AddField(scroll, "Stable identifier / URL", _identifier, ref y);
        AddField(scroll, "Locator", _locator, ref y);
        AddMemoGroup(scroll, "Source quotation or corpus passage", _quotation,
            "Text from the source. Keep your interpretation in Reading notes below.", 115, ref y);
        AddMemoGroup(scroll, "Your reading notes", _notes,
            "Your observations and interpretation; saved separately from the source text.", 135, ref y);
        _promotionLine.SetBounds(10, y, 680, 40);
        _promotionLine.ForeColor = ReadingTheme.MutedText;
        scroll.Controls.Add(_promotionLine);
        host.Controls.Add(scroll);
        host.Controls.Add(buttons);
    }

    private async Task LoadAsync(long selectId = 0)
    {
        var questions = await _researchRepo.GetQuestionsAsync(_project.ResearchProjectId);
        var choices = new List<QuestionChoice> { new(null, "General project reading") };
        choices.AddRange(questions.Select(q => new QuestionChoice(q.ResearchQuestionId, q.Text)));
        _question.DataSource = choices;
        _evidence = await _researchRepo.GetEvidenceAsync(_project.ResearchProjectId);
        var list = await _queueRepo.GetAsync(_project.ResearchProjectId);
        _items.DataSource = list;
        if (selectId > 0)
            SelectRow(selectId);
        else if (list.Count == 0)
            ShowItem(null);
    }

    private async Task AddPassageAsync()
    {
        using var picker = new ResearchPassagePickerForm(_work);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedEdition == null || picker.SelectedNode == null)
            return;
        var edition = picker.SelectedEdition;
        var node = picker.SelectedNode;
        var item = new ResearchReadingItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchReadingKind.CorpusPassage,
            Title = $"{_work.Title} {node.CitationRef}",
            WorkCtsUrn = _work.CtsUrn,
            EditionCtsUrn = edition.CtsUrn,
            CitationRef = node.CitationRef,
            Locator = node.CitationRef,
            Quotation = node.Text,
            SortOrder = _items.Rows.Count
        };
        await _queueRepo.SaveAsync(item);
        await LoadAsync(item.ResearchReadingItemId);
    }

    private async Task AddEvidenceSourceAsync()
    {
        if (_evidence.Count == 0)
        {
            MessageBox.Show(this, "This project has no evidence sources to queue yet.", "Reading queue");
            return;
        }
        using var picker = new ReadingEvidencePickerForm(_evidence);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedEvidence == null) return;
        var source = picker.SelectedEvidence;
        var item = new ResearchReadingItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            ResearchQuestionId = source.ResearchQuestionId,
            Kind = ResearchReadingKind.EvidenceSource,
            Title = source.Title,
            LinkedEvidenceItemId = source.EvidenceItemId,
            StableIdentifier = source.StableIdentifier,
            Locator = source.CanonicalReference,
            Quotation = source.Excerpt,
            SortOrder = _items.Rows.Count
        };
        await _queueRepo.SaveAsync(item);
        await LoadAsync(item.ResearchReadingItemId);
    }

    private void NewExternal()
    {
        _items.ClearSelection();
        ShowItem(new ResearchReadingItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchReadingKind.ExternalSource,
            SortOrder = _items.Rows.Count
        });
        _title.Focus();
    }

    private async Task<bool> SaveAsync()
    {
        if (_editing == null) return false;
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            MessageBox.Show(this, "A reading item needs a title.", "Reading queue");
            return false;
        }
        _editing.Title = _title.Text.Trim();
        _editing.Status = (ResearchReadingStatus)_status.SelectedItem!;
        _editing.Priority = (ResearchReadingPriority)_priority.SelectedItem!;
        _editing.ResearchQuestionId = (_question.SelectedItem as QuestionChoice)?.Id;
        _editing.Purpose = Clean(_purpose.Text);
        _editing.StableIdentifier = Clean(_identifier.Text);
        _editing.Locator = Clean(_locator.Text);
        // Capture both controls before awaiting persistence or rebinding the
        // grid. They are deliberately separate: source text is never a
        // substitute for the researcher's reading note.
        var sourceQuotation = Clean(_quotation.Text);
        var readingNotes = Clean(_notes.Text);
        _editing.Quotation = sourceQuotation;
        _editing.Notes = readingNotes;
        await _queueRepo.SaveAsync(_editing);
        await LoadAsync(_editing.ResearchReadingItemId);
        return true;
    }

    private async Task RemoveAsync()
    {
        if (_editing?.ResearchReadingItemId is not > 0) return;
        if (MessageBox.Show(this, $"Remove “{_editing.Title}” from the reading queue?", "Remove reading",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await _queueRepo.DeleteAsync(_editing.ResearchReadingItemId);
        await LoadAsync();
    }

    private async Task OpenSourceAsync()
    {
        if (_editing == null) return;
        if (_editing.Kind == ResearchReadingKind.CorpusPassage)
        {
            var target = await new TextNodeRepository().FindByWorkUrnAndCitationAsync(
                _editing.WorkCtsUrn ?? _work.CtsUrn, _editing.CitationRef ?? "");
            if (target == null)
            {
                MessageBox.Show(this, "That stable citation is not available in the current corpus.", "Open passage");
                return;
            }
            NavigationTarget = target;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        if (_editing.LinkedEvidenceItemId is long evidenceId)
        {
            var evidence = _evidence.FirstOrDefault(e => e.EvidenceItemId == evidenceId);
            if (evidence != null)
            {
                using var form = new EvidenceSourcesForm(evidence);
                form.ShowDialog(this);
                return;
            }
        }
        if (TryGetOpenUri(_editing.StableIdentifier, _editing.Locator, out var uri))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return;
        }
        MessageBox.Show(this, "Add an http(s) identifier or locator, or link this reading to an evidence source first.", "Open source");
    }

    private async Task PromoteAsync()
    {
        if (_editing?.ResearchReadingItemId is not > 0) return;
        if (!await SaveAsync()) return;
        if (_editing.PromotedEvidenceItemId != null)
        {
            MessageBox.Show(this, "This reading has already been promoted to evidence.", "Promote reading");
            return;
        }
        if (_editing.Status != ResearchReadingStatus.Reviewed)
        {
            MessageBox.Show(this, "Mark the reading Reviewed before promoting it. This keeps collection separate from judgment.", "Promote reading");
            return;
        }
        if (string.IsNullOrWhiteSpace(_editing.Quotation) && string.IsNullOrWhiteSpace(_editing.Notes))
        {
            MessageBox.Show(this, "Add a quotation or reading note before promoting this item.", "Promote reading");
            return;
        }
        var linked = _editing.LinkedEvidenceItemId is long linkedId
            ? _evidence.FirstOrDefault(e => e.EvidenceItemId == linkedId)
            : null;
        var evidence = new EvidenceItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            ResearchQuestionId = _editing.ResearchQuestionId,
            Title = _editing.Title,
            Type = _editing.Kind == ResearchReadingKind.CorpusPassage ? EvidenceType.PrimaryText
                : _editing.Kind == ResearchReadingKind.EvidenceSource ? EvidenceType.Scholarship : EvidenceType.Other,
            SourceType = _editing.Kind == ResearchReadingKind.CorpusPassage ? "Reading queue — CTS passage" : "Reading queue — reviewed source",
            StableIdentifier = _editing.EditionCtsUrn ?? _editing.StableIdentifier ?? linked?.StableIdentifier,
            CanonicalReference = _editing.CitationRef ?? _editing.Locator ?? linked?.CanonicalReference,
            Provenance = _editing.Kind == ResearchReadingKind.CorpusPassage
                ? $"ClassicaCodex corpus passage {_editing.WorkCtsUrn}:{_editing.CitationRef}; edition {_editing.EditionCtsUrn}."
                : linked == null ? "Promoted from the research reading queue after human review."
                    : $"Reading note derived from evidence item “{linked.Title}” after human review.",
            Excerpt = _editing.Quotation,
            ResearcherNote = _editing.Notes,
            Judgment = EvidenceJudgment.Uncertain,
            Relationship = EvidenceRelationship.Contextualizes,
            Origin = EvidenceOrigin.Manual,
            SortOrder = _evidence.Count
        };
        await _researchRepo.SaveEvidenceAsync(evidence);
        await _queueRepo.MarkPromotedAsync(_editing.ResearchReadingItemId, evidence.EvidenceItemId);
        PromotedEvidenceItemId = evidence.EvidenceItemId;
        await LoadAsync(_editing.ResearchReadingItemId);
        MessageBox.Show(this, "Promoted as unreviewed evidence. Set its relationship and judgment in the Research Bench.", "Evidence created");
    }

    private void ShowItem(ResearchReadingItem? item)
    {
        _editing = item;
        _kindLine.Text = item == null ? "Select a reading or add one." : $"Kind: {Friendly(item.Kind)}";
        _title.Text = item?.Title ?? "";
        _status.SelectedItem = item?.Status ?? ResearchReadingStatus.Queued;
        _priority.SelectedItem = item?.Priority ?? ResearchReadingPriority.Normal;
        if (_question.DataSource is IEnumerable<QuestionChoice> questions)
            _question.SelectedItem = questions.FirstOrDefault(q => q.Id == item?.ResearchQuestionId) ?? questions.FirstOrDefault();
        _purpose.Text = item?.Purpose ?? "";
        _identifier.Text = item?.StableIdentifier ?? item?.EditionCtsUrn ?? "";
        _locator.Text = item?.Locator ?? item?.CitationRef ?? "";
        _quotation.Text = item?.Quotation ?? "";
        _notes.Text = item?.Notes ?? "";
        _promotionLine.Text = item?.PromotedEvidenceItemId is long id
            ? $"Promoted to evidence #{id}. Further edits here do not silently rewrite that evidence."
            : "Not evidence yet. Promotion requires Reviewed status and a quotation or note.";
    }

    private void SelectRow(long id)
    {
        foreach (DataGridViewRow row in _items.Rows)
            if (row.DataBoundItem is ResearchReadingItem item && item.ResearchReadingItemId == id)
            {
                row.Selected = true;
                _items.CurrentCell = row.Cells[0];
                break;
            }
    }

    private static Button ActionButton(string text, int width) => new() { Text = text, Width = width, Height = 28 };
    private static string Friendly(ResearchReadingKind kind) => kind switch
    {
        ResearchReadingKind.CorpusPassage => "corpus passage",
        ResearchReadingKind.EvidenceSource => "linked evidence source",
        _ => "external source"
    };
    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static bool TryGetOpenUri(string? identifier, string? locator, out Uri uri)
    {
        foreach (var candidate in new[] { identifier, locator })
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            {
                uri = parsed;
                return true;
            }

        if (!string.IsNullOrWhiteSpace(identifier) &&
            identifier.StartsWith("doi:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate("https://doi.org/" + identifier[4..].Trim(), UriKind.Absolute, out var doi))
        {
            uri = doi;
            return true;
        }

        uri = null!;
        return false;
    }

    private static void AddField(Control host, string label, TextBox box, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 680, Height = 20 }); y += 20;
        box.SetBounds(10, y, 680, 26); box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 36;
    }

    private static void AddArea(Control host, string label, TextBox box, int height, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 680, Height = 20 }); y += 20;
        box.SetBounds(10, y, 680, height); box.Multiline = true; box.ScrollBars = ScrollBars.Vertical;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += height + 10;
    }

    private static void AddMemoGroup(Control host, string title, TextBox box,
        string explanation, int height, ref int y)
    {
        var group = new GroupBox
        {
            Text = title,
            Left = 10,
            Top = y,
            Width = 680,
            Height = height,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        var help = new Label
        {
            Text = explanation,
            Left = 9,
            Top = 21,
            Width = 650,
            Height = 19,
            ForeColor = ReadingTheme.MutedText,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        box.SetBounds(9, 42, 660, height - 51);
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        group.Controls.Add(help);
        group.Controls.Add(box);
        host.Controls.Add(group);
        y += height + 10;
    }

    private static void AddCombo(Control host, string label, ComboBox box, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 680, Height = 20 }); y += 20;
        box.SetBounds(10, y, 680, 26); box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 36;
    }

    private static void AddComboPair(Control host, string leftLabel, ComboBox left, object leftValues,
        string rightLabel, ComboBox right, object rightValues, ref int y)
    {
        host.Controls.Add(new Label { Text = leftLabel, Left = 10, Top = y, Width = 250, Height = 20 });
        host.Controls.Add(new Label { Text = rightLabel, Left = 360, Top = y, Width = 250, Height = 20 }); y += 20;
        left.SetBounds(10, y, 330, 26); right.SetBounds(360, y, 330, 26);
        left.DropDownStyle = right.DropDownStyle = ComboBoxStyle.DropDownList;
        left.DataSource = leftValues; right.DataSource = rightValues;
        host.Controls.Add(left); host.Controls.Add(right); y += 36;
    }

    private sealed record QuestionChoice(long? Id, string Text) { public override string ToString() => Text; }
}

internal sealed class ReadingEvidencePickerForm : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    public EvidenceItem? SelectedEvidence => _list.SelectedItem as EvidenceItem;

    public ReadingEvidencePickerForm(IReadOnlyList<EvidenceItem> evidence)
    {
        Text = "Queue an evidence source";
        Width = 620; Height = 440; MinimumSize = new Size(500, 350); StartPosition = FormStartPosition.CenterParent;
        _list.DataSource = evidence.ToList(); _list.DisplayMember = nameof(EvidenceItem.Title);
        var add = new Button { Text = "Queue source", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
        AcceptButton = add; _list.DoubleClick += (_, _) => { if (_list.SelectedItem != null) DialogResult = DialogResult.OK; };
        Controls.Add(_list); Controls.Add(add); ReadingTheme.AttachTo(this); WindowShortcuts.CloseOnEscape(this);
    }
}

internal sealed class ResearchPassagePickerForm : Form
{
    private readonly Work _work;
    private readonly ComboBox _editions = new();
    private readonly TextBox _filter = new();
    private readonly DataGridView _nodes = new();
    private List<TextNode> _allNodes = [];
    public Edition? SelectedEdition => (_editions.SelectedItem as EditionChoice)?.Edition;
    public TextNode? SelectedNode => _nodes.CurrentRow?.DataBoundItem as TextNode;

    public ResearchPassagePickerForm(Work work)
    {
        _work = work; Text = $"Choose passage — {work.Title}"; Width = 900; Height = 650;
        MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterParent;
        var top = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(8) };
        _editions.SetBounds(8, 8, 410, 26); _editions.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.SetBounds(428, 8, 440, 26); _filter.PlaceholderText = "Filter citation or text";
        top.Controls.AddRange([_editions, _filter]);
        _nodes.Dock = DockStyle.Fill; _nodes.AutoGenerateColumns = false; _nodes.ReadOnly = true;
        _nodes.AllowUserToAddRows = false; _nodes.AllowUserToDeleteRows = false; _nodes.RowHeadersVisible = false;
        _nodes.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _nodes.MultiSelect = false;
        _nodes.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TextNode.CitationRef), HeaderText = "Citation", Width = 120 });
        _nodes.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(TextNode.Text), HeaderText = "Passage", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var choose = new Button { Text = "Add to reading queue", Dock = DockStyle.Bottom, Height = 38, DialogResult = DialogResult.OK };
        AcceptButton = choose; _nodes.DoubleClick += (_, _) => { if (SelectedNode != null) DialogResult = DialogResult.OK; };
        _editions.SelectedIndexChanged += async (_, _) => await LoadNodesAsync();
        _filter.TextChanged += (_, _) => ApplyFilter();
        Controls.Add(_nodes); Controls.Add(top); Controls.Add(choose);
        ReadingTheme.AttachTo(this); WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            var editions = await new EditionRepository().GetByWorkAsync(_work.WorkId);
            _editions.DataSource = editions.Select(edition => new EditionChoice(edition)).ToList();
            if (editions.Count == 0) MessageBox.Show(this, "No ingested editions are available for this work.", "Choose passage");
        };
    }

    private async Task LoadNodesAsync()
    {
        if (SelectedEdition == null) return;
        _allNodes = await new TextNodeRepository().GetByEditionAsync(SelectedEdition.EditionId);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _filter.Text.Trim();
        _nodes.DataSource = string.IsNullOrEmpty(query) ? _allNodes
            : _allNodes.Where(n => n.CitationRef.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                   n.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private sealed record EditionChoice(Edition Edition)
    {
        public override string ToString()
        {
            var description = new List<string>();
            if (!string.IsNullOrWhiteSpace(Edition.Language))
                description.Add(Edition.Language);
            description.Add(Edition.Kind.ToString());
            if (!string.IsNullOrWhiteSpace(Edition.Translator))
                description.Add($"trans. {Edition.Translator}");
            return $"{string.Join(" · ", description)} — {Edition.CtsUrn}";
        }
    }
}
