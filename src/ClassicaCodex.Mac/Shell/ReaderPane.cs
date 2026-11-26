using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Shell;

/// <summary>Which side of the reader a pane is.</summary>
public enum PaneRole { Original, Translation }

/// <summary>
/// One page of the reader: the edition picker across the top, the passages,
/// and the reference of the selected passage along the bottom.
///
/// The Mac counterpart of the Windows SyncListView plus the combo and label
/// MainForm docks around it. Most of what SyncListView does is in service of
/// drawing variable-height rows in a WinForms list - measuring, cutting,
/// caching layouts - and none of that survives the move, because Avalonia's
/// virtualising list measures each passage itself. What does survive is the
/// reading behaviour: the bracketed-line italics, the marks after a passage,
/// the citation margin, right-to-left scripts, linked scrolling by passage.
/// </summary>
public sealed class ReaderPane : UserControl
{
    private readonly ListBox _list;
    private readonly TextBlock _message;
    private readonly TextBlock _reference;
    private readonly TextBlock _count;
    private readonly TextBlock _roleLabel;
    private List<PassageRow> _rows = new();
    private Dictionary<string, PassageMarks> _marks = new(StringComparer.Ordinal);
    private bool _programmaticSelection;
    private ScrollViewer? _scroller;
    private bool _showCitationMargin = CitationMarginSettings.Enabled;

    public ReaderPane(PaneRole role)
    {
        Role = role;

        EditionPicker = new ComboBox
        {
            Classes = { "edition" },
            PlaceholderText = role == PaneRole.Original ? "No original-language edition" : "No translation",
            MaxDropDownHeight = 420
        };

        _roleLabel = new TextBlock
        {
            Text = RoleLabelFor(role, null),
            Classes = { "section" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 4, 0)
        };

        var header = new DockPanel { Height = 40 };
        DockPanel.SetDock(_roleLabel, Dock.Left);
        header.Children.Add(_roleLabel);
        header.Children.Add(EditionPicker);
        EditionPicker.VerticalAlignment = VerticalAlignment.Center;
        EditionPicker.Margin = new Thickness(0, 0, 8, 0);

        var headerBorder = new Border
        {
            Child = header,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Cc.PageHeader"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Cc.Rule"));

        _reference = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _count = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var footer = new DockPanel { Height = 28, Margin = new Thickness(14, 0) };
        DockPanel.SetDock(_count, Dock.Right);
        footer.Children.Add(_count);
        footer.Children.Add(_reference);

        var footerBorder = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBorder.Bind(Border.BorderBrushProperty, footerBorder.GetResourceObservable("Cc.Rule"));

        _list = new ListBox
        {
            Classes = { "reader" },
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(6, 8),
            // The template is asked for a null item while the list is being cleared
            // - switching works, showing a message - and answers it with nothing.
            ItemTemplate = new FuncDataTemplate<PassageRow>((row, _) => row == null ? new Border() : BuildRow(row), supportsRecycling: false)
        };
        _list.SelectionChanged += OnSelectionChanged;
        _list.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(HookScroller);

        _message = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 380,
            IsVisible = false
        };

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_message);

        var dock = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        dock.Children.Add(headerBorder);
        dock.Children.Add(footerBorder);
        dock.Children.Add(body);

        // The page's paper: Codex's vellum or linen grain, laid under the text
        // and faint enough to read as warmth rather than pattern. Behind the
        // list, not in it, so selection and hover still draw in flat colour.
        var paper = new Border { IsHitTestVisible = false };
        void LayPaper()
        {
            var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            paper.Background = Common.Art.PageTexture(dark);
            paper.Opacity = dark ? 0.5 : 0.9;
        }

        ActualThemeVariantChanged += (_, _) => LayPaper();
        LayPaper();

        var layered = new Panel();
        layered.Children.Add(paper);
        layered.Children.Add(dock);

        Content = new Border { Classes = { "page" }, Child = layered };

        _list.FontFamily = ReadingFont;
        ApplyFontSize();
    }

    public PaneRole Role { get; }

    public ComboBox EditionPicker { get; }

    /// <summary>The list itself, for the main window to hang the passage menu on.</summary>
    public ListBox List => _list;

    /// <summary>The edition currently filling the pane, or null when it shows a message.</summary>
    public Edition? Edition { get; private set; }

    /// <summary>The word over the pane, as the reader sees it.</summary>
    internal string RoleText => _roleLabel.Text ?? string.Empty;

    /// <summary>
    /// What the label over a pane says. The second pane is the translation's
    /// until it holds an original: where a work has two originals and no
    /// translation the reader puts the second one there - Oracc's signs beside
    /// their transliteration, see ReaderPaneRules.SplitEditionsBetweenPanes -
    /// and a label still saying TRANSLATION would claim an English text the
    /// library does not have.
    /// </summary>
    internal static string RoleLabelFor(PaneRole role, Edition? edition)
    {
        var isOriginal = edition == null ? role == PaneRole.Original : edition.Kind == EditionKind.Original;
        return isOriginal ? "ORIGINAL" : "TRANSLATION";
    }

    /// <summary>Raised when the reader - not the program - selects a passage.</summary>
    public event Action<ReaderPane, int>? UserSelected;

    /// <summary>Raised when the passage at the top of the pane changes.</summary>
    public event Action<ReaderPane>? TopChanged;

    /// <summary>
    /// Raised when the reader clicks quoted words in a passage: the row, and
    /// every quotation those words belong to - usually one, but two links can
    /// cover the same words.
    /// </summary>
    public event Action<ReaderPane, PassageRow, IReadOnlyList<QuotedSpan>>? QuotationActivated;

    public int Count => _rows.Count;

    public int SelectedIndex => _list.SelectedIndex;

    public TextNode? SelectedNode => NodeAt(_list.SelectedIndex);

    public TextNode? NodeAt(int index) => index >= 0 && index < _rows.Count ? _rows[index].Node : null;

    /// <summary>The marks drawn after the passage at <paramref name="index"/>.</summary>
    public PassageMarks MarksAt(int index) => index >= 0 && index < _rows.Count ? _rows[index].Marks : PassageMarks.None;

    /// <summary>The quoted words in the passage at <paramref name="index"/>, and where they lead.</summary>
    public IReadOnlyList<QuotedSpan> QuotationsAt(int index) =>
        index >= 0 && index < _rows.Count ? _rows[index].Quotations : Array.Empty<QuotedSpan>();

    /// <summary>The face this pane reads in before any script-specific font is put in front of it.</summary>
    private FontFamily ReadingFont => (FontFamily)Application.Current!.FindResource(
        Role == PaneRole.Original ? "Cc.SourceFont" : "Cc.TranslationFont")!;

    /// <summary>
    /// Points as the Windows app stores them, in device-independent pixels as
    /// Avalonia draws them: 11pt there is the same size as 14.7 here.
    /// </summary>
    private double FontSizeDip => (Role == PaneRole.Original
        ? ReadingFontSettings.SourceSize
        : ReadingFontSettings.TranslationSize) * 96.0 / 72.0;

    public void ApplyFontSize()
    {
        _list.FontSize = FontSizeDip;
        Rebuild();
    }

    public bool ShowCitationMargin
    {
        get => _showCitationMargin;
        set
        {
            if (_showCitationMargin == value) return;
            _showCitationMargin = value;
            Rebuild();
        }
    }

    /// <summary>Puts the pane's message in place of the passages: "(no translation ingested)" and the like.</summary>
    public void ShowMessage(string message, Edition? edition = null)
    {
        Edition = edition;
        _roleLabel.Text = RoleLabelFor(Role, edition);
        _rows = new List<PassageRow>();
        _list.ItemsSource = null;
        _message.Text = message;
        _message.IsVisible = true;
        _count.Text = string.Empty;
        _reference.Text = string.Empty;
    }

    /// <summary>
    /// Fills the pane with an edition's passages, choosing the script's own
    /// font and direction from the text itself. <paramref name="quotations"/>
    /// holds, by node id, the words in a passage that quote an earlier one.
    /// </summary>
    public void Fill(
        Edition edition, IReadOnlyList<TextNode> nodes, Dictionary<string, PassageMarks> marks,
        IReadOnlyDictionary<long, IReadOnlyList<QuotedSpan>>? quotations = null)
    {
        Edition = edition;
        _roleLabel.Text = RoleLabelFor(Role, edition);
        _marks = marks;

        var scriptFamily = ScriptFonts.FamilyFor(nodes);
        _list.FontFamily = ScriptFonts.Compose(ReadingFont, scriptFamily);
        _list.FlowDirection = ScriptFonts.IsRightToLeft(nodes) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var rows = new List<PassageRow>(nodes.Count);
        TextNode? previousLine = null;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var margin = CitationMargin.MarkFor(node, previousLine);
            var row = new PassageRow(node, i, margin)
            {
                Quotations = quotations != null && quotations.TryGetValue(node.TextNodeId, out var quoted) ? quoted : Array.Empty<QuotedSpan>()
            };
            if (marks.TryGetValue(node.CitationRef, out var mark)) row.Marks = mark;
            rows.Add(row);

            if (string.Equals(row.Kind, TextNodeKinds.Line, StringComparison.Ordinal)) previousLine = node;
        }

        _rows = rows;
        _message.IsVisible = false;
        _list.ItemsSource = _rows;
        _count.Text = $"{_rows.Count:N0} passages";
        UpdateReference();
    }

    /// <summary>Replaces the marks without refilling, so the reader keeps their place.</summary>
    public void SetPassageMarks(Dictionary<string, PassageMarks> marks)
    {
        _marks = marks;
        foreach (var row in _rows)
            row.Marks = marks.TryGetValue(row.Node.CitationRef, out var mark) ? mark : PassageMarks.None;
    }

    public void AddPassageMark(string citationRef, PassageMarks mark)
    {
        _marks[citationRef] = (_marks.TryGetValue(citationRef, out var existing) ? existing : PassageMarks.None) | mark;
        foreach (var row in _rows.Where(r => r.Node.CitationRef == citationRef)) row.Marks |= mark;
    }

    public int RowOfNode(long textNodeId)
    {
        for (var i = 0; i < _rows.Count; i++)
            if (_rows[i].Node.TextNodeId == textNodeId) return i;
        return -1;
    }

    /// <summary>Selects a passage as the program, not the reader: no position is remembered, no mirroring.</summary>
    public void SelectOnly(int index)
    {
        _programmaticSelection = true;
        try
        {
            _list.SelectedIndex = index >= 0 && index < _rows.Count ? index : -1;
        }
        finally
        {
            _programmaticSelection = false;
        }

        UpdateReference();
    }

    /// <summary>
    /// Brings a passage into view, scrolling as little as it can when the
    /// passage is near, and by <see cref="ScrollToTop"/> when it is not.
    /// </summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _rows.Count) return;

        if (IsNearDrawn(index)) _list.ScrollIntoView(index);
        else ScrollToTop(index);
    }

    /// <summary>
    /// Within a screen or so of the passages drawn now. The list's own
    /// ScrollIntoView draws every passage between here and there, which costs
    /// nothing for the next one and five seconds for one fifteen thousand
    /// passages away; a jump that far goes by offset instead.
    /// </summary>
    private bool IsNearDrawn(int index)
    {
        var drawn = _list.GetRealizedContainers().Select(c => _list.IndexFromContainer(c)).Where(i => i >= 0).ToList();
        return drawn.Count > 0 && index >= drawn.Min() - NearRows && index <= drawn.Max() + NearRows;
    }

    private const int NearRows = 40;

    /// <summary>
    /// The first passage whose row reaches into the visible area - what the
    /// reader would call "where the pane is".
    /// </summary>
    public int TopIndex
    {
        get
        {
            if (_scroller == null || _rows.Count == 0) return -1;

            foreach (var container in _list.GetRealizedContainers().OrderBy(c => _list.IndexFromContainer(c)))
            {
                var position = container.TranslatePoint(new Point(0, container.Bounds.Height), _scroller);
                if (position is { } bottom && bottom.Y > TopInset + 1) return _list.IndexFromContainer(container);
            }

            return -1;
        }
    }

    /// <summary>
    /// How far below the top edge a passage scrolled "to the top" sits, so its
    /// first line is not flush against the page header. TopIndex uses the same
    /// figure, or the passage above - whose last pixels fill that gap - would
    /// count as the one at the top, and linked pages would disagree by one.
    /// </summary>
    private const double TopInset = 6;

    /// <summary>Scrolls so the passage at <paramref name="index"/> sits at the top of the pane.</summary>
    /// <remarks>
    /// In passes, because one is not enough. The list knows the height of
    /// the passages it has drawn and guesses the rest, so moving to a
    /// passage it has not drawn lands where the guess put it; drawing what is
    /// now on screen corrects the guess and moves the passage. A prose
    /// translation whose paragraphs differ in length by a factor of ten
    /// landed thirty passages short of the Greek on the first pass. Each pass
    /// measures what the last one revealed.
    ///
    /// A passage far from the ones drawn is reached by setting the offset,
    /// not by the list's ScrollIntoView. That draws every passage on the way:
    /// 37,000 of them to reach the middle of the Iliad scholia and come back,
    /// which took six seconds, and Livy's last chapter took thirty-eight with
    /// both pages following. Setting the offset draws only what lands on
    /// screen, so the distance left is estimated from the passages drawn
    /// there, and the next pass looks again.
    /// </remarks>
    public void ScrollToTop(int index)
    {
        if (_scroller == null || index < 0 || index >= _rows.Count) return;

        for (var pass = 0; pass < 10; pass++)
        {
            _list.UpdateLayout();

            double distance;
            if (_list.ContainerFromIndex(index) is { } container)
            {
                if (container.TranslatePoint(new Point(0, 0), _scroller) is not { } position) return;
                distance = position.Y - TopInset;
                if (Math.Abs(distance) < 1) return;
            }
            else if (IsNearDrawn(index) || !TryEstimateDistance(index, out distance))
            {
                _list.ScrollIntoView(index);
                continue;
            }

            var before = _scroller.Offset.Y;
            _scroller.Offset = new Vector(_scroller.Offset.X, Math.Max(0, before + distance));

            // At the end of the text the last passages cannot reach the top.
            if (Math.Abs(_scroller.Offset.Y - before) < 0.5) return;
        }
    }

    /// <summary>
    /// How far down the page <paramref name="index"/> starts, reckoned from
    /// the first passage drawn and the average height of those drawn.
    /// </summary>
    private bool TryEstimateDistance(int index, out double distance)
    {
        distance = 0;
        var drawn = _list.GetRealizedContainers()
            .Select(c => (Index: _list.IndexFromContainer(c), Control: c))
            .Where(d => d.Index >= 0 && d.Control.Bounds.Height > 0)
            .ToList();
        if (drawn.Count == 0) return false;

        var first = drawn.MinBy(d => d.Index);
        if (first.Control.TranslatePoint(new Point(0, 0), _scroller!) is not { } top) return false;

        var average = drawn.Average(d => d.Control.Bounds.Height);
        distance = top.Y + (index - first.Index) * average - TopInset;
        return true;
    }

    /// <summary>The citation of the selected passage, as the footer shows it.</summary>
    public string ReferenceText => _reference.Text ?? string.Empty;

    private void HookScroller()
    {
        if (_scroller != null) return;
        _scroller = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroller != null) _scroller.ScrollChanged += (_, _) => TopChanged?.Invoke(this);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateReference();
        if (!_programmaticSelection && _list.SelectedIndex >= 0) UserSelected?.Invoke(this, _list.SelectedIndex);
    }

    private void UpdateReference()
    {
        if (NodeAt(_list.SelectedIndex) is not { } node)
        {
            _reference.Text = string.Empty;
            return;
        }

        var reference = PassageCitation.Display(node.CitationRef, node.Milestone);

        // The doubt travels with the reference, since this is the string a
        // reader is most likely to copy out of the window.
        _reference.Text = node.IsAthetized && reference.Length > 0
            ? reference + "  —  bracketed by the editor"
            : reference;
    }

    /// <summary>Re-realises every row, after a change that alters how rows are built.</summary>
    private void Rebuild()
    {
        if (_rows.Count == 0) return;

        var selected = _list.SelectedIndex;
        var top = TopIndex;

        _list.ItemsSource = null;
        _list.ItemsSource = _rows;

        SelectOnly(selected);
        if (top >= 0) Dispatcher.UIThread.Post(() => ScrollToTop(top), DispatcherPriority.Background);
    }

    /// <summary>
    /// The visual for one passage. Text the author wrote reads plainly; the
    /// rest - speakers, stage directions, headings - is set off so a play
    /// reads like a play. A line the editor bracketed is italic and muted,
    /// which is how the Windows reader marks it and why: the brackets are not
    /// put into the text, because the text is what gets copied and counted.
    /// </summary>
    private Control BuildRow(PassageRow row)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top
        };

        // The reading face goes on the runs, because the block's own face is
        // the one its lines are measured from: see SteadyLines below. Words
        // that quote an earlier passage are a run of their own, underlined.
        var segments = QuotationSegments(row.Node.Text, row.Quotations);
        var at = 0;
        foreach (var segment in segments)
        {
            if (segment.Start > at) text.Inlines!.Add(new Run(row.Node.Text[at..segment.Start]) { FontFamily = _list.FontFamily });
            text.Inlines!.Add(new Run(row.Node.Text[segment.Start..segment.End])
            {
                FontFamily = _list.FontFamily,
                TextDecorations = QuotedUnderline(text)
            });
            at = segment.End;
        }

        if (at < row.Node.Text.Length || segments.Count == 0)
            text.Inlines!.Add(new Run(row.Node.Text[at..]) { FontFamily = _list.FontFamily });

        var marks = new Run { FontFamily = FontFamily.Default, FontSize = 12, BaselineAlignment = BaselineAlignment.Center };
        marks.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("Cc.Accent"));
        text.Inlines!.Add(marks);

        void ShowMarks() => marks.Text = row.Marks == PassageMarks.None ? string.Empty : "  " + PassageMarkSymbols.Suffix(row.Marks).Trim();
        ShowMarks();

        // Subscribed only while the row is on screen. The rows outlive their
        // visuals - a text-size change rebuilds every visual from the same
        // rows - and a handler left behind would keep each dead visual alive.
        System.ComponentModel.PropertyChangedEventHandler onMarks = (_, _) => ShowMarks();
        text.AttachedToVisualTree += (_, _) =>
        {
            row.PropertyChanged -= onMarks;
            row.PropertyChanged += onMarks;
            ShowMarks();
        };
        text.DetachedFromVisualTree += (_, _) => row.PropertyChanged -= onMarks;

        switch (row.Kind)
        {
            case TextNodeKinds.Speaker:
                text.FontWeight = FontWeight.SemiBold;
                text.FontSize = Math.Max(10, FontSizeDip * 0.84);
                text.LetterSpacing = 0.6;
                text.Margin = new Thickness(0, 6, 0, 0);
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.Accent"));
                break;
            case TextNodeKinds.Stage:
                text.FontStyle = FontStyle.Italic;
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
                break;
            case TextNodeKinds.Head:
                text.FontWeight = FontWeight.SemiBold;
                text.FontSize = FontSizeDip * 1.12;
                text.Margin = new Thickness(0, 10, 0, 4);
                break;
            case TextNodeKinds.Cast:
            case TextNodeKinds.Paratext:
                text.FontStyle = FontStyle.Italic;
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkFaint"));
                break;
            case TextNodeKinds.Attribution:
                text.FontStyle = FontStyle.Italic;
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.Accent"));
                break;
        }

        if (row.Node.IsAthetized)
        {
            text.FontStyle = FontStyle.Italic;
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
        }

        // The row is not in the list yet, so a size it inherits from the pane
        // reads as Avalonia's default of 12 here. Asked that way, every line
        // was 16.6 high whatever the reader's text size: at 12pt, lines barely
        // a hair apart. Only a speaker or a heading sets a size of its own.
        var size = text.IsSet(TextBlock.FontSizeProperty) ? text.FontSize : FontSizeDip;
        text.LineHeight = size * 1.38;

        // A Greek line holding an elision mark, which Palatino Linotype lacks,
        // otherwise drops into the line below it on Windows.
        SteadyLines.Steady(text, _list.FontFamily, size);

        ToolTip.SetTip(text, TooltipFor(row.Node, row.Quotations, row.Marks.HasFlag(PassageMarks.Dictionary)));
        ToolTip.SetShowDelay(text, 900);

        if (!_showCitationMargin) return Wrap(text, new Thickness(10, 2));

        var margin = new TextBlock
        {
            Text = row.MarginText,
            Width = 58,
            TextAlignment = TextAlignment.Right,
            FontFamily = FontFamily.Default,
            FontSize = 11,
            Margin = new Thickness(0, 3, 12, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        margin.Bind(TextBlock.ForegroundProperty, margin.GetResourceObservable("Cc.InkFaint"));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(text, 1);
        grid.Children.Add(margin);
        grid.Children.Add(text);
        return Wrap(grid, new Thickness(2, 2, 10, 2));

        // A background, even a clear one, so a press anywhere on the row
        // reaches the row before the list item that holds it.
        Control Wrap(Control child, Thickness padding)
        {
            var border = new Border { Padding = padding, Child = child, Background = Brushes.Transparent };
            if (segments.Count > 0) HookQuotations(border, text, row, segments);
            return border;
        }
    }

    /// <summary>
    /// The pointer over quoted words shows a hand, and a click on them goes
    /// to the line they quote. On the press, not the release: the list item
    /// may take the pointer on the press to select the row, and the release
    /// then never reaches the row. The jump waits until the press has
    /// selected the row, so the quoting passage is where Back returns to.
    /// </summary>
    private void HookQuotations(Border border, TextBlock text, PassageRow row, IReadOnlyList<QuotationSegment> segments)
    {
        border.PointerMoved += (_, e) =>
            border.Cursor = SegmentAt(text, e.GetPosition(text), segments) != null ? HandCursor.Value : null;
        border.PointerExited += (_, _) => border.Cursor = null;
        border.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 1 || e.KeyModifiers != KeyModifiers.None) return;
            if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
            if (SegmentAt(text, e.GetPosition(text), segments) is not { } segment) return;

            Dispatcher.UIThread.Post(() => QuotationActivated?.Invoke(this, row, segment.Spans));
        };
    }

    private static readonly Lazy<Cursor> HandCursor = new(() => new Cursor(StandardCursorType.Hand));

    /// <summary>
    /// The quoted stretch under the pointer, if any. Asked of each stretch's
    /// own rectangles rather than of the layout's point test, which reports a
    /// point on the second line of a wrapped passage as outside the text
    /// whenever the line is set taller than its font - as every reader line is.
    /// </summary>
    private static QuotationSegment? SegmentAt(TextBlock text, Point point, IReadOnlyList<QuotationSegment> segments)
    {
        var layout = text.TextLayout;
        foreach (var segment in segments)
        foreach (var rect in layout.HitTestTextRange(segment.Start, segment.End - segment.Start))
            if (rect.Inflate(new Thickness(0, 2)).Contains(point)) return segment;

        return null;
    }

    /// <summary>
    /// Clicks the quoted words at <paramref name="characterIndex"/> in the
    /// passage at <paramref name="index"/>, as a press on them would - for the
    /// tests, which cannot always aim a pointer at a word.
    /// </summary>
    internal bool ActivateQuotation(int index, int characterIndex)
    {
        if (index < 0 || index >= _rows.Count) return false;

        var row = _rows[index];
        foreach (var segment in QuotationSegments(row.Node.Text, row.Quotations))
        {
            if (characterIndex < segment.Start || characterIndex >= segment.End) continue;
            QuotationActivated?.Invoke(this, row, segment.Spans);
            return true;
        }

        return false;
    }

    /// <summary>A stretch of quoted words, and every quotation it belongs to.</summary>
    internal readonly record struct QuotationSegment(int Start, int End, IReadOnlyList<QuotedSpan> Spans);

    /// <summary>
    /// The quoted stretches of a passage, overlapping quotations merged into
    /// one: two links found through two names often cover nearly the same
    /// words, and two underlines that half overlap read as neither. Positions
    /// that do not fit the text are left out rather than trusted.
    /// </summary>
    internal static List<QuotationSegment> QuotationSegments(string text, IReadOnlyList<QuotedSpan> spans)
    {
        var segments = new List<QuotationSegment>();
        foreach (var span in spans.Where(s => s.Start >= 0 && s.End <= text.Length && s.Start < s.End).OrderBy(s => s.Start))
        {
            if (segments.Count > 0 && span.Start < segments[^1].End)
            {
                var last = segments[^1];
                segments[^1] = new QuotationSegment(last.Start, Math.Max(last.End, span.End), last.Spans.Append(span).ToList());
            }
            else
            {
                segments.Add(new QuotationSegment(span.Start, span.End, new List<QuotedSpan> { span }));
            }
        }

        return segments;
    }

    /// <summary>A thin underline in the accent colour, for words that quote an earlier passage.</summary>
    private static TextDecorationCollection QuotedUnderline(TextBlock owner)
    {
        var line = new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            StrokeThickness = 1,
            StrokeThicknessUnit = TextDecorationUnit.Pixel,
            StrokeOffset = 2,
            StrokeOffsetUnit = TextDecorationUnit.Pixel
        };
        line.Bind(TextDecoration.StrokeProperty, owner.GetResourceObservable("Cc.Accent"));
        return new TextDecorationCollection { line };
    }

    private static string TooltipFor(TextNode node, IReadOnlyList<QuotedSpan> quotations, bool citedInDictionary)
    {
        var citation = PassageCitation.Bracketed(node.CitationRef, node.Milestone);
        var tip = node.IsAthetized ? citation + " - bracketed by the editor as probably not authentic" : citation;
        if (citedInDictionary) tip += "\nCited in LSJ or Lewis & Short - right-click for Cited in the Dictionaries.";
        if (quotations.Count == 0) return tip;

        var quoted = quotations.Select(q => q.Label).Distinct(StringComparer.Ordinal).ToList();
        var named = string.Join("; ", quoted.Take(3)) + (quoted.Count > 3 ? $"; and {quoted.Count - 3} more" : string.Empty);
        return $"{tip}\nQuotes {named} - found by machine. Click the underlined words to go there.";
    }
}
