using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Research.Bench;
using ClassicaCodex.Mac.Features.Research.Sources;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Reading;

/// <summary>A staging area for sources and passages that have not yet become evidence.</summary>
internal sealed class ReadingQueueWindow : ResearchWindow
{
    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly ResearchReadingQueueRepository _queueRepo = new();
    private readonly ResearchRepository _researchRepo = new();
    private readonly DataGrid _items = Ui.Table();
    private readonly TextBox _title = Ui.Line();
    private readonly ComboBox _status = Ui.EnumCombo<ResearchReadingStatus>();
    private readonly ComboBox _priority = Ui.EnumCombo<ResearchReadingPriority>();
    private readonly ComboBox _question = Ui.Combo(Ui.QuestionChoices("General project reading", Array.Empty<ResearchQuestion>()));
    private readonly TextBox _purpose = Ui.Area(65);
    private readonly TextBox _identifier = Ui.Line();
    private readonly TextBox _locator = Ui.Line();
    private readonly TextBox _quotation = Ui.Area(100);
    private readonly TextBox _notes = Ui.Area(120);
    private readonly TextBlock _kindLine = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _promotionLine = Ui.Muted(string.Empty);
    private ResearchReadingItem? _editing;
    private List<ResearchReadingItem> _list = new();
    private List<EvidenceItem> _evidence = new();
    private bool _loading;

    public ReadingQueueWindow(IReaderHost? host, ResearchProject project, Work work) : base(host)
    {
        _project = project;
        _work = work;
        Title = $"Reading Queue — {project.Name}";
        Width = 1280;
        Height = 800;
        MinWidth = 1050;
        MinHeight = 650;

        var notice = Ui.Notice("Reading queue · passages and sources stay provisional until you review and promote them to evidence.");
        notice.Margin = new Thickness(16, 12, 16, 10);

        var dock = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        dock.Children.Add(notice);
        dock.Children.Add(Ui.Split(BuildList(), BuildEditor(), 455, 380, 560));
        Content = dock;

        Opened += (_, _) => Run(() => LoadAsync());
    }

    /// <summary>The evidence made by the last promotion, for the Bench to show when this closes.</summary>
    public long? PromotedEvidenceItemId { get; private set; }

    private Control BuildList()
    {
        var passage = Ui.Button("Add Passage…", "Plus");
        var source = Ui.Button("Queue Evidence…", "ListBullets");
        var external = Ui.Button("New External", "OpenExternal");
        var remove = Ui.Button("Remove", "Minus");
        passage.Click += (_, _) => Run(AddPassageAsync);
        source.Click += (_, _) => Run(AddEvidenceSourceAsync);
        external.Click += (_, _) => NewExternal();
        remove.Click += (_, _) => Run(RemoveAsync);

        var actions = new WrapPanel { Margin = new Thickness(10, 0, 10, 8) };
        foreach (var button in new[] { passage, source, external, remove })
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            actions.Children.Add(button);
        }

        _items.Columns.Add(Ui.Column("Priority", nameof(ReadingRow.Priority), 82));
        _items.Columns.Add(Ui.Column("Status", nameof(ReadingRow.Status), 84));
        _items.Columns.Add(Ui.Column("Reading", nameof(ReadingRow.Title)));
        _items.Margin = new Thickness(10, 0, 10, 10);
        _items.SelectionChanged += (_, _) =>
        {
            // The editor must never bind to a different reading than the one
            // highlighted, or "Save notes" would rewrite the wrong row.
            if (!_loading) ShowItem(SelectedItem);
        };

        var dock = new DockPanel();
        DockPanel.SetDock(actions, Dock.Top);
        dock.Children.Add(actions);
        dock.Children.Add(_items);
        return dock;
    }

    private Control BuildEditor()
    {
        var form = new StackPanel();
        form.Children.Add(_kindLine);
        form.Children.Add(Ui.Field("Title", _title));
        form.Children.Add(Ui.Pair(Ui.Field("Status", _status), Ui.Field("Priority", _priority)));
        form.Children.Add(Ui.Field("Research question", _question));
        form.Children.Add(Ui.Field("Why read this?", _purpose));
        form.Children.Add(Ui.Pair(Ui.Field("Stable identifier / URL", _identifier), Ui.Field("Locator", _locator)));
        _quotation.Bind(TextBox.FontFamilyProperty, _quotation.GetResourceObservable("Cc.SourceFont"));
        _quotation.FontSize = 14;
        form.Children.Add(MemoGroup("Source quotation or corpus passage",
            "Text from the source. Keep your interpretation in Reading notes below.", _quotation));
        form.Children.Add(MemoGroup("Your reading notes",
            "Your observations and interpretation; saved separately from the source text.", _notes));
        form.Children.Add(_promotionLine);

        var save = Ui.Button("Save Notes", "Check", accent: true, tip: "Save the reading (" + Ui.Chord("S") + ")");
        var open = Ui.Button("Open Source", "OpenExternal");
        var promote = Ui.Button("Promote to Evidence", "Seal");
        save.Click += (_, _) => Run(async () => await SaveAsync());
        open.Click += (_, _) => Run(OpenSourceAsync);
        promote.Click += (_, _) => Run(PromoteAsync);

        var bar = Ui.ButtonBar(Ui.Row(open, promote), save);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(Ui.Scroll(form));
        return dock;
    }

    /// <summary>A titled box for a memo, with the line that says what belongs in it.</summary>
    private static Control MemoGroup(string title, string explanation, TextBox box)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "h3" } });
        stack.Children.Add(Ui.Caption(explanation));
        stack.Children.Add(box);
        return new Border { Classes = { "panel" }, Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    protected override bool OnCommandKey(Key key, bool shift)
    {
        if (key != Key.S) return false;
        Run(async () => await SaveAsync());
        return true;
    }

    /// <summary>The reading the table is actually showing as selected.</summary>
    private ResearchReadingItem? SelectedItem => (_items.SelectedItem as ReadingRow)?.Item;

    internal async Task LoadAsync(long selectId = 0)
    {
        var questions = await _researchRepo.GetQuestionsAsync(_project.ResearchProjectId);
        Ui.SetChoices(_question, Ui.QuestionChoices("General project reading", questions), Ui.Value<long?>(_question));
        _evidence = await _researchRepo.GetEvidenceAsync(_project.ResearchProjectId);
        _list = await _queueRepo.GetAsync(_project.ResearchProjectId);
        var rows = _list.Select(i => new ReadingRow(i)).ToList();

        _loading = true;
        ReadingRow? selected;
        try
        {
            _items.ItemsSource = rows;
            selected = (selectId > 0 ? rows.FirstOrDefault(r => r.Item.ResearchReadingItemId == selectId) : null) ?? rows.FirstOrDefault();
            Ui.Reveal(_items, selected);
        }
        finally
        {
            _loading = false;
        }

        // Bound explicitly to the row selected: the selection event does not fire
        // when the target row is already the selection.
        ShowItem(selected?.Item);
    }

    private async Task AddPassageAsync()
    {
        var picker = new PassagePickerWindow(_work);
        await picker.ShowDialog(this);
        if (picker.SelectedEdition is not { } edition || picker.SelectedNode is not { } node) return;
        await AddPassageAsync(edition, node);
    }

    internal async Task AddPassageAsync(Edition edition, TextNode node)
    {
        var item = new ResearchReadingItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchReadingKind.CorpusPassage,
            Title = $"{_work.Title} {PassageCitation.Display(node.CitationRef, node.Milestone)}",
            WorkCtsUrn = _work.CtsUrn,
            EditionCtsUrn = edition.CtsUrn,
            CitationRef = node.CitationRef,
            Locator = node.CitationRef,
            Quotation = node.Text,
            SortOrder = _list.Count
        };
        await _queueRepo.SaveAsync(item);
        await LoadAsync(item.ResearchReadingItemId);
    }

    private async Task AddEvidenceSourceAsync()
    {
        if (_evidence.Count == 0)
        {
            await Alert.Info(this, "Reading queue", "This project has no evidence sources to queue yet.");
            return;
        }

        var picker = new ReadingEvidencePickerWindow(_evidence);
        await picker.ShowDialog(this);
        if (picker.SelectedEvidence is not { } source) return;

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
            SortOrder = _list.Count
        };
        await _queueRepo.SaveAsync(item);
        await LoadAsync(item.ResearchReadingItemId);
    }

    private void NewExternal()
    {
        _loading = true;
        try
        {
            _items.SelectedItem = null;
        }
        finally
        {
            _loading = false;
        }

        ShowItem(new ResearchReadingItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchReadingKind.ExternalSource,
            SortOrder = _list.Count
        });
        _title.Focus();
    }

    internal async Task<bool> SaveAsync()
    {
        if (_editing == null) return false;
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            await Alert.Info(this, "Reading queue", "A reading item needs a title.");
            return false;
        }

        _editing.Title = _title.Text.Trim();
        _editing.Status = Ui.Value<ResearchReadingStatus>(_status);
        _editing.Priority = Ui.Value<ResearchReadingPriority>(_priority);
        _editing.ResearchQuestionId = Ui.Value<long?>(_question);
        _editing.Purpose = Ui.Clean(_purpose.Text);
        _editing.StableIdentifier = Ui.Clean(_identifier.Text);
        _editing.Locator = Ui.Clean(_locator.Text);

        // Capture both boxes before awaiting persistence or rebinding the table.
        // They are deliberately separate: source text is never a substitute for
        // the researcher's reading note.
        var sourceQuotation = Ui.Clean(_quotation.Text);
        var readingNotes = Ui.Clean(_notes.Text);
        _editing.Quotation = sourceQuotation;
        _editing.Notes = readingNotes;
        await _queueRepo.SaveAsync(_editing);
        await LoadAsync(_editing.ResearchReadingItemId);
        return true;
    }

    private async Task RemoveAsync()
    {
        if (_editing?.ResearchReadingItemId is not > 0) return;
        if (!await ConfirmAsync("Remove reading", $"Remove “{_editing.Title}” from the reading queue?", "Remove", AlertKind.Warning)) return;

        await _queueRepo.DeleteAsync(_editing.ResearchReadingItemId);
        await LoadAsync();
    }

    internal async Task OpenSourceAsync()
    {
        if (_editing == null) return;

        if (_editing.Kind == ResearchReadingKind.CorpusPassage)
        {
            var target = await new TextNodeRepository().FindByWorkUrnAndCitationAsync(
                _editing.WorkCtsUrn ?? _work.CtsUrn, _editing.CitationRef ?? string.Empty);
            if (target == null)
            {
                await Alert.Info(this, "Open passage", "That stable citation is not available in the current corpus.");
                return;
            }

            // Straight to the reader, which this window sits beside; it stays open
            // so the reading can be annotated against the passage.
            await NavigateAsync(target.Value.WorkId, target.Value.TextNodeId);
            return;
        }

        if (_editing.LinkedEvidenceItemId is long evidenceId)
        {
            var evidence = _evidence.FirstOrDefault(e => e.EvidenceItemId == evidenceId);
            if (evidence != null)
            {
                ResearchWindows.Show($"sources:{evidence.EvidenceItemId}", this, () => new EvidenceSourcesWindow(evidence));
                return;
            }
        }

        if (ResearchLinks.TryGetOpenUri(_editing.StableIdentifier, _editing.Locator, out var uri))
        {
            MacShell.Open(uri.AbsoluteUri);
            return;
        }

        await Alert.Info(this, "Open source", "Add an http(s) identifier or locator, or link this reading to an evidence source first.");
    }

    internal async Task PromoteAsync()
    {
        if (_editing?.ResearchReadingItemId is not > 0) return;
        if (!await SaveAsync()) return;

        if (_editing.PromotedEvidenceItemId != null)
        {
            await Alert.Info(this, "Promote reading", "This reading has already been promoted to evidence.");
            return;
        }

        if (_editing.Status != ResearchReadingStatus.Reviewed)
        {
            await Alert.Info(this, "Promote reading", "Mark the reading Reviewed before promoting it. This keeps collection separate from judgment.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_editing.Quotation) && string.IsNullOrWhiteSpace(_editing.Notes))
        {
            await Alert.Info(this, "Promote reading", "Add a quotation or reading note before promoting this item.");
            return;
        }

        var linked = _editing.LinkedEvidenceItemId is long linkedId ? _evidence.FirstOrDefault(e => e.EvidenceItemId == linkedId) : null;
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
                : linked == null
                    ? "Promoted from the research reading queue after human review."
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
        await Alert.Info(this, "Evidence created", "Promoted as unreviewed evidence. Set its relationship and judgment in the Research Bench.");
    }

    private void ShowItem(ResearchReadingItem? item)
    {
        _editing = item;
        _kindLine.Text = item == null ? "Select a reading or add one." : $"Kind: {Friendly(item.Kind)}";
        _title.Text = item?.Title ?? string.Empty;
        Ui.Select(_status, item?.Status ?? ResearchReadingStatus.Queued);
        Ui.Select(_priority, item?.Priority ?? ResearchReadingPriority.Normal);
        Ui.Select(_question, item?.ResearchQuestionId);
        _purpose.Text = item?.Purpose ?? string.Empty;
        _identifier.Text = item?.StableIdentifier ?? item?.EditionCtsUrn ?? string.Empty;
        _locator.Text = item?.Locator ?? item?.CitationRef ?? string.Empty;
        _quotation.Text = item?.Quotation ?? string.Empty;
        _notes.Text = item?.Notes ?? string.Empty;
        _promotionLine.Text = item?.PromotedEvidenceItemId is long id
            ? $"Promoted to evidence #{id}. Further edits here do not silently rewrite that evidence."
            : "Not evidence yet. Promotion requires Reviewed status and a quotation or note.";
    }

    private static string Friendly(ResearchReadingKind kind) => kind switch
    {
        ResearchReadingKind.CorpusPassage => "corpus passage",
        ResearchReadingKind.EvidenceSource => "linked evidence source",
        _ => "external source"
    };

    // ── What the tests drive and read ─────────────────────────────────────

    internal IReadOnlyList<ResearchReadingItem> Items => _list;
    internal ResearchReadingItem? Editing => _editing;

    internal void Edit(ResearchReadingStatus? status = null, string? notes = null)
    {
        if (status is { } s) Ui.Select(_status, s);
        if (notes != null) _notes.Text = notes;
    }

    private sealed class ReadingRow
    {
        public ReadingRow(ResearchReadingItem item) => Item = item;

        public ResearchReadingItem Item { get; }
        public string Priority => Words.Of(Item.Priority);
        public string Status => Words.Of(Item.Status);
        public string Title => Item.Title;
    }
}
