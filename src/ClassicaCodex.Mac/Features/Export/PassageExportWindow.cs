using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Export;

/// <summary>
/// Opened from a reader pane's right-click menu (and File → Export Passage,
/// ⌘E). Starts from the clicked line and lets you grow the export to however
/// many consecutive lines make up the passage you actually want, previews
/// exactly what will be written, then saves it as .txt, .docx or .pdf - or
/// as a reference for a reference manager.
///
/// The Mac counterpart of PassageExportForm, behaviour for behaviour.
/// </summary>
public sealed class PassageExportWindow : FeatureWindow
{
    private readonly TextNode _startNode;
    private readonly int _editionId;
    private readonly string _authorName;
    private readonly string _workTitle;
    private readonly string _fontFamilies;
    private readonly int? _counterpartEditionId;
    private readonly bool _counterpartIsOriginal;

    /// <summary>
    /// How the counterpart edition is named in the reader - "trans. Gemini
    /// (AI-generated)", "trans. Samuel Butler", "Greek (original)". Null only
    /// when the caller could not resolve the edition, in which case export
    /// falls back to the old bare label rather than inventing attribution.
    /// </summary>
    private readonly string? _counterpartDescriptor;

    /// <summary>
    /// The original-language reading font. A bilingual document has to set
    /// polytonic Greek whichever pane was right-clicked, and the translation
    /// pane's Georgia has no polytonic coverage - it would come out as
    /// fallback glyphs or boxes. Palatino sets both scripts, so it is the safe
    /// choice for mixed output.
    /// </summary>
    private readonly string _originalFontFamilies;

    private readonly RadioButton _lineCountMode;
    private readonly RadioButton _toEndMode;
    private readonly RadioButton _entireWorkMode;
    private readonly NumericUpDown _lineCount;
    private readonly CheckBox _showCitations;
    private readonly CheckBox _combine;
    private readonly CheckBox _bilingual;

    /// <summary>
    /// One box per kind of node the edition contains, beyond the text itself.
    ///
    /// Separate from the reader's own toggles rather than following them. What
    /// you want on screen and what you want in a file are different questions:
    /// a reader with the stage directions switched off may still be exporting
    /// a scene for someone staging it. They start where the reader is, because
    /// that is the better guess, and then go their own way.
    /// </summary>
    private readonly List<(string Kind, CheckBox Box)> _kindBoxes = new();

    private readonly WrapPanel _kindPanel;
    private readonly StackPanel _kindSection;
    private readonly TextBox _preview;
    private readonly FormatPicker _formats;
    private readonly TextBlock _status;

    private readonly TextNodeRepository _textNodeRepo = new();
    private List<(string CitationRef, string Text, string? Milestone)> _currentLines = new();

    /// <summary>
    /// Aligns the counterpart edition's passages against the primary by
    /// citation ref - see PassageAligner's own remarks for why an exact-only
    /// match isn't enough. Null when bilingual mode is off or there's no
    /// counterpart edition to align against.
    /// </summary>
    private PassageAligner? _aligner;

    /// <summary>The counterpart edition, read once: it does not change while the window is open.</summary>
    private PassageAligner? _counterpartAligner;

    /// <summary>Which refresh is current; a slower one that finishes later gives way.</summary>
    private int _refreshGeneration;

    public PassageExportWindow(
        TextNode startNode, int editionId, string authorName, string workTitle, string fontFamilies,
        int? counterpartEditionId = null, bool counterpartIsOriginal = false, string? originalFontFamilies = null,
        string? counterpartDescriptor = null)
    {
        _startNode = startNode;
        _editionId = editionId;
        _authorName = authorName;
        _workTitle = workTitle;
        _fontFamilies = fontFamilies;
        _counterpartEditionId = counterpartEditionId;
        _counterpartIsOriginal = counterpartIsOriginal;
        _counterpartDescriptor = counterpartDescriptor;
        _originalFontFamilies = originalFontFamilies ?? fontFamilies;

        Title = "Export Passage";
        Width = 680;
        Height = 780;
        MinWidth = 560;
        MinHeight = 620;

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock { Text = $"{authorName}, {workTitle}", Classes = { "h2" }, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock
        {
            Text = $"Starting at [{PassageCitation.Display(startNode.CitationRef, startNode.Milestone)}]",
            Classes = { "caption" }
        });

        _lineCountMode = new RadioButton { GroupName = "export-scope", Content = "Number of lines:", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        _lineCount = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 5000,
            Value = 1,
            Increment = 1,
            FormatString = "0",
            Width = 120,
            Margin = new Thickness(8, 0, 24, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _toEndMode = new RadioButton { GroupName = "export-scope", Content = "From here to end of work", VerticalAlignment = VerticalAlignment.Center };
        _entireWorkMode = new RadioButton { GroupName = "export-scope", Content = "Entire work (from the beginning)" };

        _lineCount.ValueChanged += async (_, _) => await RefreshPreviewAsync();
        _lineCountMode.IsCheckedChanged += async (_, _) =>
        {
            _lineCount.IsEnabled = _lineCountMode.IsChecked == true;
            if (_lineCountMode.IsChecked == true) await RefreshPreviewAsync();
        };
        _toEndMode.IsCheckedChanged += async (_, _) =>
        {
            if (_toEndMode.IsChecked == true) await RefreshPreviewAsync();
        };
        _entireWorkMode.IsCheckedChanged += async (_, _) =>
        {
            if (_entireWorkMode.IsChecked == true) await RefreshPreviewAsync();
        };

        var firstScopeRow = new StackPanel { Orientation = Orientation.Horizontal };
        firstScopeRow.Children.Add(_lineCountMode);
        firstScopeRow.Children.Add(_lineCount);
        firstScopeRow.Children.Add(_toEndMode);

        var scope = new StackPanel { Spacing = 6 };
        scope.Children.Add(firstScopeRow);
        scope.Children.Add(_entireWorkMode);

        _showCitations = new CheckBox { Content = "Show citation refs", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
        _showCitations.IsCheckedChanged += (_, _) => RefreshPreview();

        _combine = new CheckBox { Content = "Combine into one continuous passage" };
        _combine.IsCheckedChanged += (_, _) => RefreshPreview();

        _bilingual = new CheckBox
        {
            Content = "Include both original and translation",
            IsEnabled = counterpartEditionId != null
        };
        _bilingual.IsCheckedChanged += async (_, _) => await RefreshPreviewAsync();

        if (counterpartEditionId == null)
        {
            ToolTip.SetTip(_bilingual, "This work doesn't have both an original and a translation loaded in the reader.");
            ToolTip.SetShowOnDisabled(_bilingual, true);
        }

        var layoutRow = new WrapPanel();
        layoutRow.Children.Add(_showCitations);
        layoutRow.Children.Add(_combine);

        var layout = new StackPanel { Spacing = 4 };
        layout.Children.Add(layoutRow);
        layout.Children.Add(_bilingual);

        // Filled once the edition's nodes have been read and it is known
        // which kinds are actually in it.
        _kindPanel = new WrapPanel();
        _kindSection = new StackPanel { Spacing = 4, IsVisible = false };
        _kindSection.Children.Add(Section("INCLUDE"));
        _kindSection.Children.Add(_kindPanel);

        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(fontFamilies),
            FontSize = 13.5,
            Padding = new Thickness(10, 8)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        _formats = new FormatPicker(includeReferences: true);

        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        var export = new Button
        {
            Content = IconLabel("Export", "Export…"),
            Classes = { "accent" },
            IsDefault = true,
            MinWidth = 96
        };
        export.Click += async (_, _) => await ExportAsync();

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(close);
        buttons.Children.Add(export);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);

        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(header);
        top.Children.Add(Section("RANGE", new Thickness(0, 10, 0, 0)));
        top.Children.Add(scope);
        top.Children.Add(Section("LAYOUT", new Thickness(0, 8, 0, 0)));
        top.Children.Add(layout);
        top.Children.Add(_kindSection);
        top.Children.Add(Section("PREVIEW", new Thickness(0, 8, 0, 0)));

        var formatSection = new StackPanel { Spacing = 6, Margin = new Thickness(0, 14, 0, 0) };
        formatSection.Children.Add(Section("EXPORT AS"));
        formatSection.Children.Add(_formats);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(formatSection, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(formatSection);
        root.Children.Add(_preview);

        Content = root;

        Opened += async (_, _) => await RefreshPreviewAsync();
    }

    /// <summary>The format the export will write, for the tests to choose without a click.</summary>
    internal ExportFormat Format
    {
        get => _formats.Selected;
        set => _formats.Selected = value;
    }

    internal NumericUpDown LineCount => _lineCount;

    internal CheckBox Bilingual => _bilingual;

    internal string PreviewText => _preview.Text ?? string.Empty;

    internal string StatusText => _status.Text ?? string.Empty;

    private static TextBlock Section(string text, Thickness? margin = null) =>
        new() { Text = text, Classes = { "section" }, Margin = margin ?? new Thickness(0) };

    private static StackPanel IconLabel(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Glyphs.Icon(glyph, 14));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private void SetStatus(string text, bool warning = false)
    {
        _status.Text = text;
        _status.Classes.Set("warning", warning);
        _status.Classes.Set("caption", !warning);
    }

    /// <summary>
    /// Positions in the counterpart edition aligned with this citation ref -
    /// delegates to the shared PassageAligner; see its remarks for why exact
    /// matching alone isn't enough.
    /// </summary>
    private List<int>? ResolveCounterpartIndices(string citationRef) => _aligner?.ResolveIndices(citationRef);

    /// <summary>
    /// Adds a checkbox for any kind of node seen that does not have one yet.
    ///
    /// Grown rather than built once, because the scope controls change which
    /// nodes are loaded: a five-line range from the middle of a play contains
    /// no cast list, and switching to the whole work should offer one rather
    /// than silently exporting it because no box existed to untick. Boxes are
    /// never removed for the same reason in reverse - a choice already made
    /// should not disappear when the range narrows.
    ///
    /// Each starts where the reader has it, which is the better guess than
    /// on-by-default, and is free to diverge from there.
    /// </summary>
    private void SyncKindCheckboxes(IEnumerable<TextNode> nodes)
    {
        var present = nodes
            .Select(n => string.IsNullOrWhiteSpace(n.NodeKind) ? TextNodeKinds.Line : n.NodeKind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => !_kindBoxes.Any(c => string.Equals(c.Kind, k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (present.Count > 0)
        {
            var ordered = NodeKindVisibility.InMenuOrder(_kindBoxes.Select(c => c.Kind).Concat(present)).ToList();
            foreach (var kind in ordered)
            {
                if (_kindBoxes.Any(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase))) continue;

                var box = new CheckBox
                {
                    Content = NodeKindVisibility.Label(kind),
                    IsChecked = NodeKindVisibility.IsVisible(kind),
                    Margin = new Thickness(0, 0, 16, 0)
                };
                box.IsCheckedChanged += async (_, _) => await RefreshPreviewAsync();
                _kindBoxes.Add((kind, box));
            }

            // Laid out in menu order, which a new kind can land in the middle of.
            _kindPanel.Children.Clear();
            foreach (var kind in NodeKindVisibility.InMenuOrder(_kindBoxes.Select(c => c.Kind)))
                _kindPanel.Children.Add(_kindBoxes.First(c => c.Kind == kind).Box);
        }

        // Nothing to choose between when the edition is only text.
        _kindSection.IsVisible = _kindBoxes.Count > 1;
    }

    /// <summary>
    /// Whether a kind is included. Unknown kinds are included: a box is only
    /// absent because nothing of that kind has been seen yet, and the default
    /// everywhere else is that a node the parser produced is a node the reader
    /// gets.
    /// </summary>
    private bool KindIsChecked(string nodeKind)
    {
        var kind = string.IsNullOrWhiteSpace(nodeKind) ? TextNodeKinds.Line : nodeKind;
        var match = _kindBoxes.FirstOrDefault(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase));
        return match.Box?.IsChecked ?? true;
    }

    internal async Task RefreshPreviewAsync()
    {
        var generation = ++_refreshGeneration;
        var requestedCount = (int)(_lineCount.Value ?? 1);

        List<TextNode> nodes;
        try
        {
            if (_entireWorkMode.IsChecked == true)
            {
                // Whole edition from its first line, not from wherever the
                // right-click happened to be.
                nodes = await _textNodeRepo.GetByEditionAsync(_editionId);
            }
            else
            {
                var lineCount = _toEndMode.IsChecked == true ? int.MaxValue : requestedCount;
                nodes = await _textNodeRepo.GetRangeAsync(_editionId, _startNode.TextNodeId, lineCount);
            }

            // Citation ref is the only shared key between an original and its
            // translation - they don't share line numbering, IDs, or ordering.
            // Loaded lazily, and for the whole counterpart edition at once,
            // since a lookup by citation needs the full map anyway and an
            // edition is at most a few thousand rows.
            if (_bilingual.IsChecked == true && _counterpartEditionId != null && _counterpartAligner == null)
            {
                _counterpartAligner = new PassageAligner(await _textNodeRepo.GetByEditionAsync(_counterpartEditionId.Value));
            }
        }
        catch (Exception ex)
        {
            if (generation == _refreshGeneration) SetStatus($"Couldn't read the passage: {ex.Message}", warning: true);
            return;
        }

        if (generation != _refreshGeneration) return;

        SyncKindCheckboxes(nodes);

        // Held before filtering: "only N available from here to the end" is a
        // statement about the range, and unticking Stage directions does not
        // mean the edition ran out of lines.
        var loadedCount = nodes.Count;
        nodes = nodes.Where(n => KindIsChecked(n.NodeKind)).ToList();

        _currentLines = nodes.Select(n => (n.CitationRef, n.Text, n.Milestone)).ToList();
        _aligner = _bilingual.IsChecked == true && _counterpartEditionId != null ? _counterpartAligner : null;

        RefreshPreview();

        if (_bilingual.IsChecked == true && _aligner != null)
        {
            var matched = _currentLines.Count(l => ResolveCounterpartIndices(l.CitationRef) != null);
            SetStatus(matched == _currentLines.Count
                ? $"{nodes.Count} line(s), all paired."
                : _entireWorkMode.IsChecked == true
                    ? $"{nodes.Count} line(s); {matched} paired by citation ref. Unpaired translation " +
                      "passages (introductions, cast lists) are included too, because this is the whole work."
                    : $"{nodes.Count} line(s); {matched} paired by citation ref. The rest have no " +
                      "translation passage to pair with, and nothing else from the translation is included.");
        }
        else
        {
            SetStatus(_lineCountMode.IsChecked == true && loadedCount < requestedCount
                ? $"Only {loadedCount} line(s) available from here to the end of the edition."
                : $"{nodes.Count} line(s).");
        }
    }

    /// <summary>
    /// Rebuilds just the preview text from already-fetched lines - used
    /// when a toggle changes, so flipping "Show citation refs" or "Combine"
    /// doesn't need a database round-trip.
    /// </summary>
    private void RefreshPreview()
    {
        var chunks = BuildRenderChunks();
        _preview.Text = string.Join(Environment.NewLine,
            chunks.Select(c => string.IsNullOrEmpty(c.Label) ? c.Text : $"{c.Label} {c.Text}"));
    }

    /// <summary>
    /// Which counterpart passages belong in an export of these lines.
    ///
    /// The whole work takes the whole counterpart edition, and that is the
    /// point: a translation carries an introduction, a cast list, a chapter
    /// heading over sections the original numbers one by one, and none of it
    /// pairs with anything. Leave it out of a whole-work export and the
    /// translation is missing its own front matter.
    ///
    /// Any narrower scope takes only what the exported lines actually resolve
    /// to. Both export layouts used to take everything regardless: the
    /// combined one joined every counterpart passage in the edition, and the
    /// interleaved one swept up everything before the first match and then
    /// everything left over. Exporting a single passage with its translation
    /// therefore wrote out the whole translation - which is what a reader
    /// reported after exporting one passage and finding several translated
    /// ones in the file.
    /// </summary>
    internal static List<int> CounterpartIndicesForExport(
        IReadOnlyList<string> citationRefs, PassageAligner aligner, bool wholeWork)
    {
        if (wholeWork) return Enumerable.Range(0, aligner.Ordered.Count).ToList();

        // Sorted and distinct: reading order, and a counterpart that several
        // exported lines resolve to appears once.
        var chosen = new SortedSet<int>();
        foreach (var citationRef in citationRefs)
        {
            var indices = aligner.ResolveIndices(citationRef);
            if (indices == null) continue;
            foreach (var index in indices) chosen.Add(index);
        }

        return chosen.ToList();
    }

    /// <summary>
    /// Turns the fetched lines into whatever the toggles say the output
    /// should actually look like - shared by the preview and every export
    /// format, so they can never drift out of sync with each other.
    ///
    /// Bilingual mode pairs each line with its counterpart by citation ref,
    /// which is the only key the two editions genuinely share (they don't
    /// share IDs, line numbering, or line counts). That pairing is
    /// deliberately partial: a verse original is lineated per line while a
    /// prose translation often carries one citation per paragraph, so many
    /// original lines simply have no counterpart at that exact ref. Those
    /// are emitted alone rather than dropped or guessed at - showing the
    /// original with no translation beside it is honest; inventing an
    /// alignment that isn't in the source data wouldn't be.
    /// </summary>
    private List<(string Label, string Text)> BuildRenderChunks()
    {
        if (_currentLines.Count == 0) return new();

        var bilingual = _bilingual.IsChecked == true && _aligner != null;
        var showCitations = _showCitations.IsChecked == true;
        var wholeWork = _entireWorkMode.IsChecked == true;

        // The counterpart is labelled with its actual edition - translator
        // included - rather than a bare "trans.". Exported text outlives the
        // application that produced it, so whether a rendering is Butler's,
        // the reader's own, or a machine's has to travel with it.
        var counterpartLabel = _counterpartDescriptor ?? (_counterpartIsOriginal ? "original" : "trans.");

        if (_combine.IsChecked == true)
        {
            var rangeLabel = !showCitations ? string.Empty
                : _currentLines.Count == 1 ? $"[{PassageCitation.Display(_currentLines[0].CitationRef, _currentLines[0].Milestone)}]"
                : $"[{PassageCitation.Display(_currentLines.First().CitationRef, _currentLines.First().Milestone)}–{PassageCitation.Display(_currentLines.Last().CitationRef, _currentLines.Last().Milestone)}]";

            var primaryText = string.Join(" ", _currentLines.Select(l => l.Text));

            if (!bilingual) return new List<(string, string)> { (rangeLabel, primaryText) };

            // Combined + bilingual reads best as two continuous blocks - the
            // whole passage in one language, then the whole passage in the
            // other. Each counterpart passage appears once, in its own reading
            // order; for the whole work that is all of them, so introductions
            // and cast lists survive even though nothing in the original pairs
            // with them, and for a narrower scope it is what the exported
            // lines resolve to and nothing more.
            var counterpartText = string.Join(" ", CounterpartIndicesForExport(
                    _currentLines.Select(l => l.CitationRef).ToList(), _aligner!, wholeWork)
                .Select(i => _aligner!.Ordered[i].Text));

            var result = new List<(string, string)> { (rangeLabel, primaryText) };
            if (counterpartText.Length > 0) result.Add(($"({counterpartLabel})", counterpartText));
            return result;
        }

        var chunks = new List<(string Label, string Text)>();

        if (!bilingual)
        {
            foreach (var line in _currentLines)
            {
                var soloLabel = showCitations ? $"[{PassageCitation.Display(line.CitationRef, line.Milestone)}]" : string.Empty;
                chunks.Add((soloLabel, line.Text));
            }

            return chunks;
        }

        // Merge both sequences rather than walking the primary and hanging
        // counterparts off it. Iterating the primary alone silently drops
        // every counterpart passage that has no primary equivalent - which
        // is exactly what introductions, prefatory arguments, and cast lists
        // are, since they usually appear only in the translation. Tracking
        // what's been emitted lets those appear in their proper position.
        var counterpartOrdered = _aligner!.Ordered;
        var emitted = new bool[counterpartOrdered.Count];
        var cursor = 0;

        void EmitCounterpart(int index)
        {
            if (index < 0 || index >= counterpartOrdered.Count || emitted[index]) return;
            emitted[index] = true;
            chunks.Add(($"({counterpartLabel})", counterpartOrdered[index].Text));
        }

        // Sweeping in the counterpart passages that matched nothing is right
        // for the whole work and wrong for anything less: export five lines,
        // or one, and the translation's front matter does not belong with
        // them. The loops that gathered it used to run regardless of scope,
        // so exporting a single passage with its translation wrote out the
        // whole translation.
        foreach (var line in _currentLines)
        {
            var indices = ResolveCounterpartIndices(line.CitationRef);

            if (wholeWork && indices is { Count: > 0 })
            {
                // Anything in the counterpart edition standing before this
                // match and still unemitted belongs here - ahead of the line
                // it precedes, not appended as an afterthought at the end.
                var firstIndex = indices.Min();
                for (; cursor < firstIndex; cursor++) EmitCounterpart(cursor);
            }

            var label = showCitations ? $"[{PassageCitation.Display(line.CitationRef, line.Milestone)}]" : string.Empty;
            chunks.Add((label, line.Text));

            if (indices == null) continue;

            // Emitted once each, so a coarse counterpart spanning many
            // primary lines - an English chapter over a dozen Latin sections -
            // appears alongside the first line it covers, not under each.
            foreach (var index in indices) EmitCounterpart(index);
        }

        // Whatever's left: counterpart passages that follow the last matched
        // line, or never matched anything at all. Only for the whole work.
        if (wholeWork)
        {
            for (var i = 0; i < counterpartOrdered.Count; i++) EmitCounterpart(i);
        }

        return chunks;
    }

    /// <summary>
    /// The line written at the foot of an export, naming the edition the text
    /// came from.
    ///
    /// It used to be one fixed sentence - "Perseus Digital Library (via
    /// Classica Codex)" - for every passage ever exported. That is not a
    /// citation. This library deliberately holds the same work more than
    /// once: CSEL's critical text and Migne's reprint of it, Perseus and
    /// First1KGreek's copies of the Agamemnon, an original beside three
    /// translations of it. Quote a variant reading from an export that does
    /// not say which, and there is no way afterwards to say where the reading
    /// came from.
    ///
    /// The CTS URN carries that, which is what CTS URNs exist for. The
    /// collection is named alongside it because the URN alone will not tell
    /// a reader that stoa0223.stoa001 is Migne rather than CSEL, and the
    /// translator because a translation without one attributed is the thing
    /// this application refuses to produce anywhere else.
    ///
    /// Falls back to the old sentence if the edition cannot be read, rather
    /// than inventing provenance.
    /// </summary>
    private async Task<string> DescribeSourceAsync()
    {
        const string fallback =
            "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";

        try
        {
            var edition = await new EditionRepository().GetByIdAsync(_editionId);
            if (edition == null) return fallback;

            var parts = new List<string>();

            // Editions are stored without their namespace and the work keeps
            // it, so the identifier is put back together before it goes into
            // something meant to be cited.
            var workUrn = await new WorkRepository().GetCtsUrnAsync(edition.WorkId);
            var editionUrn = CtsUrns.Qualify(workUrn, edition.CtsUrn);

            if (!string.IsNullOrWhiteSpace(editionUrn)) parts.Add($"edition {editionUrn}");
            if (!string.IsNullOrWhiteSpace(edition.Translator)) parts.Add($"trans. {edition.Translator}");
            if (!string.IsNullOrWhiteSpace(edition.Collection))
                parts.Add(SetupDataSourceCatalog.DescribeCollection(edition.Collection!));

            if (parts.Count == 0) return fallback;

            var version = typeof(PassageExportWindow).Assembly.GetName().Version;
            var stamp = version == null ? "Classica Codex" : $"Classica Codex {version.Major}.{version.Minor}.{version.Build}";

            return $"{string.Join(", ", parts)}. Retrieved with {stamp} from the Perseus Digital Library " +
                   "and the collections named in About, where the licensing for each is set out.";
        }
        catch (Exception)
        {
            // An export is not worth failing over a provenance lookup.
            return fallback;
        }
    }

    /// <summary>
    /// The passage as a reference, for a reference manager.
    ///
    /// One entry for the run on screen rather than one per line: a reader
    /// exporting Aeneid 6.847-853 wants a citation of that passage, not seven
    /// citations of seven lines. The edition is looked up rather than
    /// assumed, for the reason DescribeSourceAsync gives.
    /// </summary>
    private async Task ExportReferenceAsync(string fileName, bool asBibTeX)
    {
        try
        {
            var first = PassageCitation.Display(_currentLines.First().CitationRef, _currentLines.First().Milestone);
            var last = PassageCitation.Display(_currentLines.Last().CitationRef, _currentLines.Last().Milestone);
            var locator = first == last || last.Length == 0 ? first : $"{first}-{last}";

            string? editionUrn = null;
            string? translator = null;
            string? collection = null;

            var edition = await new EditionRepository().GetByIdAsync(_editionId);
            if (edition != null)
            {
                var workUrn = await new WorkRepository().GetCtsUrnAsync(edition.WorkId);
                editionUrn = CtsUrns.Qualify(workUrn, edition.CtsUrn);
                translator = edition.Translator;
                collection = string.IsNullOrWhiteSpace(edition.Collection)
                    ? null
                    : SetupDataSourceCatalog.DescribeCollection(edition.Collection!);
            }

            var floruit = AuthorEraData.Lookup(_authorName) is { } era
                ? $"{AuthorEraData.FormatYear(era.StartYear)}-{AuthorEraData.FormatYear(era.EndYear)}"
                : null;

            var reference = new PassageReference(_authorName, _workTitle, locator, editionUrn, translator, collection, floruit);

            var text = asBibTeX
                ? BibliographyExport.ToBibTeX(new[] { reference })
                : BibliographyExport.ToRis(new[] { reference });

            await File.WriteAllTextAsync(fileName, text, new System.Text.UTF8Encoding(false));

            SetStatus($"Saved a {(asBibTeX ? "BibTeX" : "RIS")} reference to {Path.GetFileName(fileName)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't write the reference: {ex.Message}", warning: true);
        }
    }

    private async Task ExportAsync()
    {
        if (_currentLines.Count == 0)
        {
            await Alert.Info(this, "Empty", "Nothing to export.");
            return;
        }

        var format = Format;
        var suggestedName = ExportFiles.SafeName(
            $"{_authorName} - {_workTitle} {PassageCitation.Display(_startNode.CitationRef, _startNode.Milestone)}".Replace(":", "_"));

        var path = await Files.SaveAsync(this, "Export Passage", $"{suggestedName}.{ExportFiles.Extension(format)}", ExportFiles.FileType(format));
        if (path == null) return;

        // A reference rather than the passage, which says how it went in the
        // status line: none of the font and bilingual choices mean anything
        // to a .bib file, and there is nothing to open.
        if (format is ExportFormat.BibTeX or ExportFormat.Ris)
        {
            await ExportReferenceAsync(path, asBibTeX: format == ExportFormat.BibTeX);
            return;
        }

        try
        {
            await WriteFileAsync(path, format);
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Error", $"Export failed: {ex.Message}");
            return;
        }

        await ExportFiles.OfferToRevealAsync(this, path);
    }

    /// <summary>Writes the passage as it stands in the preview. Throws if the file cannot be written.</summary>
    internal async Task WriteFileAsync(string path, ExportFormat format)
    {
        if (format is ExportFormat.BibTeX or ExportFormat.Ris)
        {
            await ExportReferenceAsync(path, asBibTeX: format == ExportFormat.BibTeX);
            return;
        }

        var first = _currentLines.First();
        var last = _currentLines.Last();
        var title = _showCitations.IsChecked == true
            ? $"{_authorName}, {_workTitle} [{PassageCitation.Display(first.CitationRef, first.Milestone)}–{PassageCitation.Display(last.CitationRef, last.Milestone)}]"
            : $"{_authorName}, {_workTitle}";
        var source = await DescribeSourceAsync();
        var chunks = BuildRenderChunks();
        var font = _bilingual.IsChecked == true ? _originalFontFamilies : _fontFamilies;

        await ExportFiles.WriteAsync(format, path, title, source, chunks, font);
    }
}
