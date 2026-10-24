using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
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
            Text = role == PaneRole.Original ? "ORIGINAL" : "TRANSLATION",
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

        Content = new Border { Classes = { "page" }, Child = dock };

        _list.FontFamily = ReadingFont;
        ApplyFontSize();
    }

    public PaneRole Role { get; }

    public ComboBox EditionPicker { get; }

    /// <summary>The list itself, for the main window to hang the passage menu on.</summary>
    public ListBox List => _list;

    /// <summary>The edition currently filling the pane, or null when it shows a message.</summary>
    public Edition? Edition { get; private set; }

    /// <summary>Raised when the reader - not the program - selects a passage.</summary>
    public event Action<ReaderPane, int>? UserSelected;

    /// <summary>Raised when the passage at the top of the pane changes.</summary>
    public event Action<ReaderPane>? TopChanged;

    public int Count => _rows.Count;

    public int SelectedIndex => _list.SelectedIndex;

    public TextNode? SelectedNode => NodeAt(_list.SelectedIndex);

    public TextNode? NodeAt(int index) => index >= 0 && index < _rows.Count ? _rows[index].Node : null;

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
        _rows = new List<PassageRow>();
        _list.ItemsSource = null;
        _message.Text = message;
        _message.IsVisible = true;
        _count.Text = string.Empty;
        _reference.Text = string.Empty;
    }

    /// <summary>
    /// Fills the pane with an edition's passages, choosing the script's own
    /// font and direction from the text itself.
    /// </summary>
    public void Fill(Edition edition, IReadOnlyList<TextNode> nodes, Dictionary<string, PassageMarks> marks)
    {
        Edition = edition;
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
            var row = new PassageRow(node, i, margin);
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

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _list.ScrollIntoView(index);
    }

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
    /// measures what the last one revealed, and three settle it.
    /// </remarks>
    public void ScrollToTop(int index)
    {
        if (_scroller == null || index < 0 || index >= _rows.Count) return;

        for (var pass = 0; pass < 4; pass++)
        {
            _list.ScrollIntoView(index);
            _list.UpdateLayout();

            if (_list.ContainerFromIndex(index) is not { } container) return;
            var position = container.TranslatePoint(new Point(0, 0), _scroller);
            if (position == null) return;

            var error = position.Value.Y - TopInset;
            if (Math.Abs(error) < 1) return;

            _scroller.Offset = new Vector(_scroller.Offset.X, Math.Max(0, _scroller.Offset.Y + error));
            _list.UpdateLayout();
        }
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

        var body = new Run(row.Node.Text);
        var marks = new Run { FontFamily = FontFamily.Default, FontSize = 12, BaselineAlignment = BaselineAlignment.Center };
        marks.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("Cc.Accent"));
        text.Inlines!.Add(body);
        text.Inlines.Add(marks);

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

        text.LineHeight = (text.FontSize > 0 && !double.IsNaN(text.FontSize) ? text.FontSize : FontSizeDip) * 1.38;

        ToolTip.SetTip(text, TooltipFor(row.Node));
        ToolTip.SetShowDelay(text, 900);

        if (!_showCitationMargin) return new Border { Padding = new Thickness(10, 2), Child = text };

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
        return new Border { Padding = new Thickness(2, 2, 10, 2), Child = grid };
    }

    private static string TooltipFor(TextNode node)
    {
        var citation = PassageCitation.Bracketed(node.CitationRef, node.Milestone);
        return node.IsAthetized ? citation + " - bracketed by the editor as probably not authentic" : citation;
    }
}
