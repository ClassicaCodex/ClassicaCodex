using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// Opening works, filling the two panes, keeping them in step, and the
/// history and reading position that let the reader find their way back.
/// Ported from MainForm's own methods of the same names, with their reasons
/// kept beside them where the reason still applies on a Mac.
/// </summary>
public partial class MainWindow
{
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly PassageMarkRepository _passageMarkRepo = new();

    // Only ever asked whether a pane's edition has anything to offer, when
    // that edition loads - see ReaderPaneCapabilities.
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly ApparatusRepository _apparatusRepo = new();

    private Work? _openWork;

    /// <summary>Whether the reader has opened something since launch; the restore gives way to it.</summary>
    private bool _readerHasNavigated;

    private bool _panesLinked = PaneSyncSettings.Enabled;

    /// <summary>
    /// The pane being scrolled to follow the other, while it moves. Its own
    /// scroll events are not the reader's and must not be mirrored back - but
    /// the leader's still are: a long jump arrives in several passes, and
    /// ignoring every event until the first settled left the follower parked
    /// wherever the leader's first pass had landed.
    /// </summary>
    private ReaderPane? _scrollFollower;
    private bool _fillingPickers;

    private (AttributionStatus Status, string? Note, bool SetByUser) _currentWorkAttribution = (AttributionStatus.Accepted, null, false);

    /// <summary>What each pane last loaded, so the kind toggles can refill it.</summary>
    private readonly Dictionary<ReaderPane, (Edition? Edition, string EmptyMessage)> _paneSource = new();

    /// <summary>Every kind of node each pane's edition contains, hidden ones included.</summary>
    private readonly Dictionary<ReaderPane, List<string>> _paneKinds = new();

    /// <summary>A translator's preface held back from the pane, offered from its menu instead.</summary>
    private readonly Dictionary<ReaderPane, (string CitationRef, string Text)?> _prefaces = new();

    /// <summary>Which fill of each pane is current; a later fill wins over one still awaiting.</summary>
    private readonly Dictionary<ReaderPane, int> _paneFillGeneration = new();

    /// <summary>What each pane's edition can support, so its menu offers only tools that will find something.</summary>
    private readonly Dictionary<ReaderPane, ReaderPaneCapabilities> _paneCapabilities = new();

    // Back and Forward. Session-only and keyed on ids, as on Windows: it does
    // not outlive the window, so the durable URN key would buy nothing.
    private readonly List<(int WorkId, long? TextNodeId)> _history = new();
    private int _historyIndex = -1;
    private bool _navigatingHistory;
    private const int MaxHistoryEntries = 100;

    /// <summary>A dropdown entry: the edition, and the label the reader sees for it.</summary>
    public sealed record EditionOption(Edition Edition, string Label)
    {
        public override string ToString() => Label;
    }

    private void WireReader()
    {
        foreach (var pane in new[] { _originalPane, _translationPane })
        {
            pane.EditionPicker.ItemTemplate = new FuncDataTemplate<EditionOption>((option, _) => new TextBlock
            {
                Text = option?.Label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var captured = pane;
            pane.EditionPicker.SelectionChanged += async (_, _) =>
            {
                if (_fillingPickers) return;
                var option = captured.EditionPicker.SelectedItem as EditionOption;
                await PopulateReaderAsync(captured, option?.Edition, EmptyMessageFor(captured));
            };

            pane.UserSelected += OnPaneUserSelected;
            pane.TopChanged += OnPaneTopChanged;
            pane.List.ContextMenu = BuildPaneMenu(pane);
        }

        LinkPanesButton.IsChecked = _panesLinked;
        RefreshLinkPanesButton();
    }

    private string EmptyMessageFor(ReaderPane pane) => pane.Role == PaneRole.Original
        ? "(no original-language edition ingested)"
        : "(no translation ingested)";

    private ReaderPane Counterpart(ReaderPane pane) => ReferenceEquals(pane, _originalPane) ? _translationPane : _originalPane;

    private void SetPanesLinked(bool linked)
    {
        _panesLinked = linked;
        PaneSyncSettings.Enabled = linked;
        LinkPanesButton.IsChecked = linked;
        RefreshLinkPanesButton();
        RefreshMenuState();
    }

    private void RefreshLinkPanesButton()
    {
        LinkPanesIcon.Data = Glyphs.Get(_panesLinked ? "Linked" : "Unlinked");
        ToolTip.SetTip(LinkPanesButton, _panesLinked
            ? "Panes are linked - they scroll and select together. Click to unlink."
            : "Panes are independent. Click to link them again.");
    }

    /// <summary>
    /// The reader chose a passage: remember where they are, and mirror the
    /// choice into the other pane if the panes are linked. Recorded whether or
    /// not they are linked - unlinking is a statement about the other pane,
    /// not about giving up one's place in this one.
    /// </summary>
    private void OnPaneUserSelected(ReaderPane source, int index)
    {
        RememberReadingPosition(source, index);
        if (!_panesLinked) return;

        var target = Counterpart(source);
        if (index < 0 || index >= target.Count) return;

        target.SelectOnly(index);
        target.EnsureVisible(index);
    }

    /// <summary>
    /// Scrolling one pane scrolls the other to the same passage. By passage,
    /// not by pixel: the two editions set their text at different lengths, and
    /// a pixel offset would drift a line further apart at every paragraph.
    /// A pane with no such passage - a shorter edition, a message - is no
    /// reason to move the other.
    /// </summary>
    private void OnPaneTopChanged(ReaderPane source)
    {
        if (!_panesLinked || ReferenceEquals(source, _scrollFollower)) return;

        // Moving the follower runs a layout pass over the whole window, which
        // can raise the leader's scroll event again from inside this one. That
        // nested event is noted rather than acted on - acting on it recursed
        // until the stack gave out - and the leader is mirrored once more
        // after, so a leader still settling is followed to where it settles.
        if (_mirroring)
        {
            _mirrorAgain = source;
            return;
        }

        _mirroring = true;
        try
        {
            for (var pass = 0; pass < 4; pass++)
            {
                _mirrorAgain = null;
                Mirror(source);
                if (!ReferenceEquals(_mirrorAgain, source)) break;
            }
        }
        finally
        {
            _mirroring = false;
        }
    }

    private bool _mirroring;
    private ReaderPane? _mirrorAgain;

    private void Mirror(ReaderPane leader)
    {
        var target = Counterpart(leader);
        if (target.Count == 0) return;

        var ordinal = leader.TopIndex;
        if (ordinal < 0 || ordinal >= target.Count) return;
        if (target.TopIndex == ordinal) return;

        _scrollFollower = target;
        target.ScrollToTop(ordinal);

        // Released after layout has run, so the scroll this causes in the
        // other pane is not taken for the reader scrolling it.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_scrollFollower, target)) _scrollFollower = null;
        }, DispatcherPriority.Background);
    }

    private void RememberReadingPosition(ReaderPane pane, int index)
    {
        if (_openWork == null) return;
        if (pane.NodeAt(index) is not { } node) return;

        ReadingPosition.Save(_openWork.CtsUrn, node.CitationRef);
    }

    /// <summary>
    /// Reopens whatever was last being read. Best-effort by design: a work
    /// since removed, a corpus re-ingested, a position saved against another
    /// library - all simply mean the app opens on the welcome page.
    /// </summary>
    private async Task RestoreReadingPositionAsync()
    {
        if (!ReadingPosition.ReopenOnLaunch) return;

        var saved = ReadingPosition.Load();
        if (saved == null) return;

        try
        {
            var target = await _textNodeRepo.FindByWorkUrnAndCitationAsync(saved.Value.WorkCtsUrn, saved.Value.CitationRef);
            if (target == null || _readerHasNavigated) return;

            await NavigateToPassageAsync(target.Value.WorkId, target.Value.TextNodeId);
        }
        catch (Exception)
        {
            // Opening at nothing is the fallback, not an error.
        }
    }

    private async Task OnLibraryWorkSelectedAsync(Work work)
    {
        _readerHasNavigated = true;
        _openWork = work;
        await LoadEditionSelectorsAsync(work.WorkId);
        RecordHistory(work.WorkId, null);
    }

    /// <summary>
    /// Opens a work in the reader without choosing a line: the library tree's
    /// selection, both panes, the toolbar's running head.
    /// </summary>
    public async Task<bool> OpenWorkAsync(int workId)
    {
        if (!Sidebar.Reveal(workId, select: true)) return false;

        var work = Sidebar.FindWork(workId);
        if (work == null) return false;

        if (_openWork?.WorkId == workId && _readerView.IsVisible) return true;

        _readerHasNavigated = true;
        _openWork = work;
        await LoadEditionSelectorsAsync(workId);
        RecordHistory(workId, null);
        return true;
    }

    /// <summary>
    /// Puts the reader on a passage, wherever the jump came from: a search
    /// result, a tag, a bookmark, an echo, the map.
    /// </summary>
    public async Task NavigateToPassageAsync(int workId, long textNodeId)
    {
        if (await NavigateToPassageCoreAsync(workId, textNodeId)) RecordHistory(workId, textNodeId);
    }

    private async Task<bool> NavigateToPassageCoreAsync(int workId, long textNodeId)
    {
        if (!await OpenWorkAsync(workId)) return false;

        if (!SelectByTextNodeId(_originalPane, textNodeId) && !SelectByTextNodeId(_translationPane, textNodeId))
        {
            // The line may belong to an edition that is not the one each pane
            // opened on - a second translation, say. Switch to it and look again.
            var editionId = await _textNodeRepo.GetEditionIdAsync(textNodeId);
            if (editionId != null)
            {
                if (await TrySelectEditionAsync(_originalPane, editionId.Value) == false)
                    await TrySelectEditionAsync(_translationPane, editionId.Value);
            }

            if (!SelectByTextNodeId(_originalPane, textNodeId) && !SelectByTextNodeId(_translationPane, textNodeId))
                await ExplainUnreachablePassageAsync(textNodeId);
        }

        return true;
    }

    private static bool SelectByTextNodeId(ReaderPane pane, long textNodeId)
    {
        var row = pane.RowOfNode(textNodeId);
        if (row < 0) return false;

        pane.SelectOnly(row);
        Dispatcher.UIThread.Post(() => pane.ScrollToTop(Math.Max(0, row - 2)), DispatcherPriority.Background);
        return true;
    }

    private async Task<bool> TrySelectEditionAsync(ReaderPane pane, int editionId)
    {
        if (pane.EditionPicker.ItemsSource is not IEnumerable<EditionOption> options) return false;

        var option = options.FirstOrDefault(o => o.Edition.EditionId == editionId);
        if (option == null) return false;

        _fillingPickers = true;
        try
        {
            pane.EditionPicker.SelectedItem = option;
        }
        finally
        {
            _fillingPickers = false;
        }

        await PopulateReaderAsync(pane, option.Edition, EmptyMessageFor(pane));
        return true;
    }

    /// <summary>
    /// Says why a jump landed on the work but not on the line: the reader has
    /// hidden that kind of line, or it is a preface held back for its menu.
    /// Arriving at the top of a long work with no explanation reads as the
    /// jump being broken.
    /// </summary>
    private async Task ExplainUnreachablePassageAsync(long textNodeId)
    {
        var editionId = await _textNodeRepo.GetEditionIdAsync(textNodeId);
        var edition = editionId == null ? null : await _editionRepo.GetByIdAsync(editionId.Value);
        var where = edition == null ? "This passage" : $"This passage is in “{EditionLabels.Descriptor(edition)}”, and it";

        await Alert.Info(this, "Passage not on screen",
            $"{where} isn't shown in the reader at the moment.\n\n" +
            "That is usually because the kind of line it is - a speaker name, a stage direction, " +
            "a heading - is currently hidden: right-click either page and use Show to bring it " +
            "back. A translator's preface is held back too, and is on the same menu under " +
            "View Preface.\n\nThe work itself is open, so nothing is lost.");
    }

    /// <summary>
    /// Fills both edition pickers for a work and both panes from them. Explicit
    /// rather than left to the pickers' change events, so that when this
    /// returns the panes are full and a caller can select a line at once.
    /// </summary>
    private async Task LoadEditionSelectorsAsync(int workId)
    {
        var editions = await _editionRepo.GetByWorkAsync(workId);

        // Everything that is not the original goes in the second pane, an
        // unclassified edition included - it is still a text of this work -
        // and where there is nothing else, the other originals do: Oracc's
        // signs beside their transliteration. The reasons are in the method.
        var (originals, translations) = ReaderPaneRules.SplitEditionsBetweenPanes(editions);

        var work = Sidebar.FindWork(workId);
        var authorName = work == null ? null : Sidebar.AuthorNameFor(work);
        var workTitle = work?.Title;

        _currentWorkAttribution = await _workRepo.GetAttributionAsync(workId);

        var originalSelected = FillPicker(_originalPane, originals, authorName, workTitle, null);
        var translationSelected = FillPicker(_translationPane, translations, authorName, workTitle,
            await BuildCoverageNotesAsync(translations, workId));

        var nothingAtAll = originals.Count == 0 && translations.Count == 0;

        await PopulateReaderAsync(_originalPane, originalSelected?.Edition, nothingAtAll
            ? "This work is named in the catalogue, but no text for it came with the downloaded data. Nothing here failed to load."
            : EmptyMessageFor(_originalPane));
        await PopulateReaderAsync(_translationPane, translationSelected?.Edition, nothingAtAll
            ? "Nothing to show - see the other page."
            : EmptyMessageFor(_translationPane));

        UpdateRunningHead();
        ShowContent(_readerView);
        RefreshMenuState();
    }

    /// <summary>
    /// Puts every edition of one kind in a picker and selects the reader's
    /// preferred collection's copy, or the first. The ordering never changes
    /// with the preference - only which row is already selected.
    /// </summary>
    private EditionOption? FillPicker(
        ReaderPane pane, List<Edition> editions, string? authorName, string? workTitle,
        IReadOnlyDictionary<int, string>? coverage)
    {
        var notes = coverage ?? new Dictionary<int, string>();

        var collisions = editions
            .GroupBy(EditionLabels.Descriptor, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToHashSet();

        var ordered = editions
            .OrderBy(EditionLabels.Descriptor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.CtsUrn, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = ordered
            .Select(e => new EditionOption(e, BuildEditionLabel(
                e, authorName, workTitle, collisions.Contains(e),
                notes.TryGetValue(e.EditionId, out var note) ? note : null,
                _currentWorkAttribution.Status)))
            .ToList();

        var index = PreferredEdition.IndexOfDefault(ordered, PreferredCollectionSettings.Preferred);

        _fillingPickers = true;
        try
        {
            pane.EditionPicker.ItemsSource = options;
            pane.EditionPicker.SelectedIndex = index;
        }
        finally
        {
            _fillingPickers = false;
        }

        return index >= 0 && index < options.Count ? options[index] : null;
    }

    /// <summary>
    /// The label for one edition. In the picker the author and work are
    /// already in the running head, so the label leads with what tells this
    /// edition from the others; doubt about the attribution and an unfinished
    /// AI translation are said where they apply.
    /// </summary>
    internal static string BuildEditionLabel(
        Edition edition, string? authorName, string? workTitle, bool disambiguate,
        string? coverageNote, AttributionStatus attribution)
    {
        var descriptor = EditionLabels.Descriptor(edition);
        if (coverageNote != null) descriptor += $" — {coverageNote}";

        if (disambiguate)
        {
            var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(suffix)) descriptor += $" ({suffix})";
        }

        var doubt = attribution switch
        {
            AttributionStatus.Disputed => "  [attribution disputed]",
            AttributionStatus.Spurious => "  [not by this author]",
            _ => string.Empty
        };

        return descriptor + doubt;
    }

    /// <summary>
    /// An author's dates as one span - "384–322 BCE", "106 BCE–43 CE" - from
    /// the same table the timeline uses; no dates rather than a guess.
    /// </summary>
    internal static string? FormatAuthorEra(string authorName)
    {
        var era = AuthorEraData.Lookup(authorName);
        if (era == null) return null;

        var (start, end) = era.Value;
        if (start < 0 && end < 0) return $"{-start}–{-end} BCE";
        if (start >= 0 && end >= 0) return $"{start}–{end} CE";
        return $"{AuthorEraData.FormatYear(start)}–{AuthorEraData.FormatYear(end)}";
    }

    private void UpdateRunningHead()
    {
        if (_openWork == null)
        {
            WorkTitle.Text = string.Empty;
            WorkSubtitle.Text = string.Empty;
            Title = "Classica Codex";
            return;
        }

        var author = Sidebar.AuthorNameFor(_openWork);
        var era = FormatAuthorEra(author);

        WorkTitle.Text = _openWork.Title;
        var subtitle = era == null ? author : $"{author} · {era}";
        if (_currentWorkAttribution.Status == AttributionStatus.Disputed) subtitle += " · attribution disputed";
        if (_currentWorkAttribution.Status == AttributionStatus.Spurious) subtitle += " · not by this author";
        WorkSubtitle.Text = subtitle;
        Title = $"{_openWork.Title} — {author}";
    }

    /// <summary>
    /// An "incomplete" note for an AI translation that does not yet cover its
    /// source - the only editions where a missing line means unfinished work.
    /// </summary>
    private async Task<Dictionary<int, string>> BuildCoverageNotesAsync(List<Edition> translations, int workId)
    {
        var notes = new Dictionary<int, string>();

        foreach (var edition in translations.Where(IsAiGenerated))
        {
            try
            {
                var coverage = await _textNodeRepo.GetTranslationCoverageAsync(edition.EditionId, workId);
                if (coverage == null) continue;

                var (translated, total) = coverage.Value;
                if (translated >= total) continue;

                notes[edition.EditionId] = $"INCOMPLETE: {translated:N0} of {total:N0} lines translated";
            }
            catch (Exception)
            {
                // A label decoration is never worth failing to open a work over.
            }
        }

        return notes;
    }

    private static bool IsAiGenerated(Edition edition) =>
        (edition.Translator?.Contains("AI-generated", StringComparison.OrdinalIgnoreCase) ?? false)
        || edition.CtsUrn.Contains(".ai-", StringComparison.OrdinalIgnoreCase);

    private async Task PopulateReaderAsync(ReaderPane pane, Edition? edition, string emptyMessage)
    {
        _paneSource[pane] = (edition, emptyMessage);
        _paneKinds[pane] = new List<string>();
        _prefaces[pane] = null;

        // Reset for the same reason as the preface: an edition that offers
        // less than the last one must not inherit its menu.
        _paneCapabilities[pane] = ReaderPaneCapabilities.Empty;

        _paneFillGeneration.TryGetValue(pane, out var previous);
        var generation = previous + 1;
        _paneFillGeneration[pane] = generation;

        if (edition == null)
        {
            pane.ShowMessage(emptyMessage);
            return;
        }

        try
        {
            // Settled here rather than when the menu opens, as on Windows:
            // both are database questions, and the answer cannot change while
            // an edition sits in a pane.
            var capabilities = ReaderPaneCapabilities.For(
                edition.Language,
                string.IsNullOrWhiteSpace(edition.Language)
                    ? 0
                    : await _lemmaRepo.CountByLanguageAsync(edition.Language),
                await _apparatusRepo.CountForEditionAsync(edition.EditionId));
            if (_paneFillGeneration[pane] != generation) return;
            _paneCapabilities[pane] = capabilities;

            var marks = await _passageMarkRepo.GetForEditionAsync(edition.EditionId, edition.CtsUrn);
            if (_paneFillGeneration[pane] != generation) return;

            var nodes = await _textNodeRepo.GetByEditionAsync(edition.EditionId);
            if (_paneFillGeneration[pane] != generation) return;

            if (nodes.Count == 0)
            {
                pane.ShowMessage("This edition was catalogued but contains no text - its source file may have failed to parse during ingest.", edition);
                return;
            }

            // A translator's preface has nothing opposite it in the original,
            // so it is held back and offered from the page's menu.
            var prefaceNodes = nodes.Where(IsPrefaceNode).ToList();
            var bodyNodes = prefaceNodes.Count == 0 ? nodes : nodes.Except(prefaceNodes).ToList();

            _paneKinds[pane] = bodyNodes
                .Select(n => string.IsNullOrWhiteSpace(n.NodeKind) ? TextNodeKinds.Line : n.NodeKind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var visible = NodeKindVisibility.Filter(bodyNodes);
            if (visible.Count == 0)
            {
                pane.ShowMessage("Everything in this edition is hidden - right-click and use Show to bring it back.", edition);
                return;
            }

            if (prefaceNodes.Count > 0)
            {
                _prefaces[pane] = (prefaceNodes[0].CitationRef,
                    string.Join(Environment.NewLine + Environment.NewLine, prefaceNodes.Select(n => n.Text)));
            }

            pane.Fill(edition, visible, marks);
        }
        catch (Exception ex)
        {
            pane.ShowMessage($"Couldn't load this edition: {ex.Message}", edition);
        }
    }

    /// <summary>
    /// What the pane's edition supports. A pane never filled reports nothing
    /// rather than everything, so a menu opened before it offers no tool that
    /// would open on nothing.
    /// </summary>
    internal ReaderPaneCapabilities CapabilitiesOf(ReaderPane pane) =>
        _paneCapabilities.TryGetValue(pane, out var capabilities) ? capabilities : ReaderPaneCapabilities.Empty;

    /// <summary>
    /// The web page the pane's text came from, when it came from one -
    /// Oracc's editions record theirs - and only an http or https address:
    /// see ReaderPaneRules.WebPageFromSourcePath.
    /// </summary>
    internal Uri? SourceWebPageFor(ReaderPane pane) =>
        ReaderPaneRules.WebPageFromSourcePath(
            _paneSource.TryGetValue(pane, out var source) ? source.Edition?.SourcePath : null);

    private static bool IsPrefaceNode(TextNode node) =>
        PassageAligner.ExtractPassageRef(node.CitationRef).StartsWith("preface", StringComparison.OrdinalIgnoreCase);

    /// <summary>Refills both panes from what they last loaded - after a Show toggle.</summary>
    private async Task RefreshReaderPanesAsync()
    {
        foreach (var (pane, source) in _paneSource.ToList())
            await PopulateReaderAsync(pane, source.Edition, source.EmptyMessage);
    }

    /// <summary>
    /// Re-reads which passages are marked, without refilling - these run when
    /// the Tags and Bookmarks windows close, which are exactly the windows the
    /// reader jumps to a passage from, and a refill would undo the jump.
    /// </summary>
    public async Task RefreshPassageMarksAsync()
    {
        foreach (var (pane, source) in _paneSource.ToList())
        {
            if (source.Edition == null) continue;
            pane.SetPassageMarks(await _passageMarkRepo.GetForEditionAsync(source.Edition.EditionId, source.Edition.CtsUrn));
        }
    }

    /// <summary>
    /// The right-clicked passage with what the tools need to know about it,
    /// or null - with a message - when nothing is selected.
    /// </summary>
    private async Task<PassageContext?> PassageContextAsync(ReaderPane pane)
    {
        if (pane.SelectedNode is not { } node)
        {
            await Alert.Info(this, "No line selected", "Select a line first.");
            return null;
        }

        var counterpart = Counterpart(pane).Edition;
        return new PassageContext(
            node,
            pane.Edition,
            counterpart,
            pane.Role == PaneRole.Original,
            pane.List.FontFamily.Name,
            pane.Edition == null ? null : EditionLabels.Descriptor(pane.Edition),
            counterpart == null ? null : EditionLabels.Descriptor(counterpart));
    }

    private (int WorkId, long? TextNodeId)? CurrentPosition()
    {
        if (_openWork == null) return null;

        foreach (var pane in new[] { _originalPane, _translationPane })
        {
            if (pane.SelectedNode is { } node) return (_openWork.WorkId, node.TextNodeId);
        }

        return (_openWork.WorkId, null);
    }

    /// <summary>
    /// Records a destination, discarding anything forward of it, as a browser
    /// does. A jump arrives twice - the work, then the line - and the second
    /// refines the first rather than being a place of its own, or every jump
    /// would cost two presses of Back.
    /// </summary>
    private void RecordHistory(int workId, long? textNodeId)
    {
        if (_navigatingHistory) return;

        if (_history.Count == 0)
        {
            var start = CurrentPosition();
            if (start != null && start.Value.WorkId != workId)
            {
                _history.Add(start.Value);
                _historyIndex = 0;
            }
        }

        var entry = (workId, textNodeId);

        if (_historyIndex >= 0)
        {
            var current = _history[_historyIndex];
            if (current.Equals(entry)) return;
            if (textNodeId == null && current.WorkId == workId) return;

            if (current.WorkId == workId && current.TextNodeId == null && textNodeId != null)
            {
                _history[_historyIndex] = entry;
                RefreshHistoryButtons();
                return;
            }
        }

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        _history.Add(entry);
        _historyIndex = _history.Count - 1;

        if (_history.Count > MaxHistoryEntries)
        {
            _history.RemoveAt(0);
            _historyIndex--;
        }

        RefreshHistoryButtons();
    }

    private async Task GoHistoryAsync(int delta)
    {
        var target = _historyIndex + delta;
        if (target < 0 || target >= _history.Count) return;

        var (workId, textNodeId) = _history[target];

        _navigatingHistory = true;
        try
        {
            var reached = textNodeId != null
                ? await NavigateToPassageCoreAsync(workId, textNodeId.Value)
                : await OpenWorkAsync(workId);

            if (!reached) return;
        }
        finally
        {
            _navigatingHistory = false;
        }

        _historyIndex = target;
        RefreshHistoryButtons();
    }

    private void RefreshHistoryButtons()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        RefreshMenuState();
    }
}
