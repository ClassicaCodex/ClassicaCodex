using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Alphabet;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// Translating a work yourself, one passage at a time, with the lexicon and
/// the grammar at hand.
///
/// This exists partly because it sidesteps a problem rather than solving it.
/// Importing somebody else's finished translation means aligning a file of
/// paragraphs onto the original's citation references, and getting that
/// slightly wrong misaligns everything downstream in a way that is very hard
/// to notice. Translating in place has no alignment step at all: the passage
/// on screen has a citation reference, and whatever gets typed belongs to it
/// by construction.
///
/// Everything in the reference panel is looked up rather than generated - the
/// headword from the lemma data, the parse from its morphology tag, the
/// entry from LSJ or Lewis and Short. That matters for trusting it: this is
/// the same information a printed commentary would give, not a guess about
/// what the passage means. The two AI buttons are the exception, say so, and
/// put what they return beside your work, never in it.
///
/// The Mac counterpart of TranslationWorkbenchForm. It opens beside the
/// reader rather than over it, one per work: two workbenches on the same
/// translation would each hold their own copy of what is written and save
/// over each other, which modality used to make impossible.
/// </summary>
public sealed class TranslationWorkbenchWindow : FeatureWindow
{
    private readonly IReaderHost _host;
    private readonly Work _work;
    private readonly string _authorName;
    private readonly Edition _translation;
    private readonly Edition _source;
    private readonly string? _sourceLanguage;

    private readonly List<TextNode> _sourcePassages;
    private readonly Dictionary<string, string> _myTranslations;

    private int _index;
    private bool _revealed;

    // Set while the passage picker is being driven by code, so its own
    // selection change doesn't treat that as a request to navigate.
    private bool _navigating;

    private bool _closeSaved;

    /// <summary>Which word lookup is current; a slower one finishing later gives way.</summary>
    private int _lookupGeneration;

    // What every citation reference in this work starts with. Some corpora
    // store a full CTS URN per line, so 45 identical characters precede the
    // part that differs and every entry in the picker reads the same.
    private readonly string _citationPrefix;

    private readonly TextBlock _headerLabel;
    private readonly TextBlock _progressLabel;
    private readonly ComboBox _gotoBox;
    private readonly List<PassageEntry> _entries;
    private readonly ScrollViewer _sourceScroller;
    private readonly StackPanel _passageStack;
    private readonly SelectableTextBlock _previousPassage;
    private readonly SelectableTextBlock _currentPassage;
    private readonly SelectableTextBlock _nextPassage;
    private readonly ListBox _sourceWords;
    private readonly ScrollViewer _reference;
    private string _referenceText = string.Empty;
    private readonly SelectableTextBlock _previousTranslation;
    private readonly TextBox _myTranslation;
    private readonly SelectableTextBlock _nextTranslation;
    private readonly Border _previousBox;
    private readonly Border _nextBox;
    private readonly ComboBox _comparisonBox;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _glossWordButton;
    private readonly Button _suggestButton;
    private readonly Button _revealButton;
    private readonly TextBlock _statusLabel;

    private readonly FontFamily _sourceFamily;
    private readonly FontFamily _translationFamily;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly WordIndexRepository _wordIndexRepo = new();
    private readonly ClassicaCodex.Ingestion.WordIndexService _wordIndexService = new();

    private PassageAligner? _publishedAligner;

    // Every other translation of this work, so the one to check against can
    // be changed without leaving. Unlike the source edition this is safe to
    // switch at any time - it is only ever read.
    private readonly List<(Edition Edition, string Label)> _comparisons;
    private readonly Func<int, Task<PassageAligner>> _loadAligner;

    /// <summary>
    /// The passage being worked on, drawn two points above the configured
    /// reading size. The neighbours sit at the configured size itself, so the
    /// one you are translating is distinguished by size as well as colour -
    /// which matters because the colour distinction is lost entirely to
    /// anyone reading in greyscale or with colour vision deficiency.
    ///
    /// Offsets rather than fixed sizes: keeping the gap rather than the
    /// numbers preserves the relationship at every reading size.
    /// </summary>
    private static double WorkingSize => Dip(ReadingFontSettings.SourceSize + 2F);

    /// <summary>
    /// The word list, one point above the reading size. It is a column of
    /// single words with no surrounding text to disambiguate them, so a
    /// diacritic missed here cannot be recovered from context the way one in
    /// the passage can.
    /// </summary>
    private static double WordListSize => Dip(ReadingFontSettings.SourceSize + 1F);

    private static double ContextSize => Dip(ReadingFontSettings.SourceSize);

    /// <summary>The translation being written, at the configured English size.</summary>
    private static double MyTranslationSize => Dip(ReadingFontSettings.TranslationSize);

    /// <summary>
    /// The translations either side, a point and a half below. Clamped so the
    /// neighbours cannot fall under the minimum at the smallest setting, where
    /// the offset would otherwise take them below the size anything is
    /// legible at.
    /// </summary>
    private static double NeighbourTranslationSize =>
        Dip(Math.Max(ReadingFontSettings.TranslationSize - 1.5F, ReadingFontSettings.MinimumSize));

    /// <summary>Points as the shared settings store them, in Avalonia's device-independent pixels.</summary>
    private static double Dip(float points) => points * 96.0 / 72.0;

    /// <summary>
    /// The key names to show. ⌘ on a Mac, and in the Mac-look screenshots the
    /// tests take; Ctrl elsewhere, where the same chords are answered with it.
    /// </summary>
    private static bool MacKeys => OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("CC_MAC_LOOK") == "1";

    public TranslationWorkbenchWindow(
        IReaderHost host, Work work, string authorName, Edition translation, Edition source,
        List<TextNode> sourcePassages, Dictionary<string, string> existingTranslations,
        List<(Edition Edition, string Label)> comparisons, Func<int, Task<PassageAligner>> loadAligner)
    {
        // The workbench deletes a passage when its box is cleared, so it is
        // only ever pointed at the reader's own translation - the caller
        // checks the kind and the ".mine-" mark, the repository refuses
        // anything that is not a translation, and this is the third look,
        // closest to the delete.
        if (translation.Kind != EditionKind.Translation || !translation.CtsUrn.Contains(".mine-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The workbench writes and deletes passages, and edition {translation.EditionId} is not a translation " +
                "the reader made here.");
        }

        _host = host;
        _work = work;
        _authorName = authorName;
        _translation = translation;
        _source = source;
        _sourceLanguage = source.Language;
        _sourcePassages = sourcePassages;
        _myTranslations = existingTranslations;
        _comparisons = comparisons;
        _loadAligner = loadAligner;
        _citationPrefix = PassageCrib.CommonCitationPrefix(sourcePassages);

        _sourceFamily = ScriptFonts.Compose((FontFamily)Application.Current!.FindResource("Cc.SourceFont")!, ScriptFonts.FamilyFor(sourcePassages));
        _translationFamily = (FontFamily)Application.Current!.FindResource("Cc.TranslationFont")!;
        var sourceDirection = ScriptFonts.IsRightToLeft(sourcePassages) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        Title = $"Translate — {authorName}, {work.Title}";
        Width = 1240;
        Height = 820;
        MinWidth = 1000;
        MinHeight = 680;

        // --- The header: where you are, and a way to go anywhere else. ---

        _headerLabel = new TextBlock { Classes = { "h2" }, TextTrimming = TextTrimming.CharacterEllipsis };
        _progressLabel = new TextBlock { Classes = { "caption" } };

        // Previous and Next alone means passage 40 of 1,646 is forty clicks
        // away, which is no way to pick at a work over months. Doubles as
        // the progress view: every passage is listed with a tick if it has
        // been done.
        _entries = sourcePassages.Select((p, i) => new PassageEntry(i, ShortCitation(p.CitationRef), Preview(p.Text), existingTranslations.ContainsKey(p.CitationRef))).ToList();
        _gotoBox = new ComboBox
        {
            Width = 320,
            MaxDropDownHeight = 480,
            ItemsSource = _entries,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<PassageEntry>((entry, _) => BuildEntry(entry, 600), supportsRecycling: false),

            // The list is far wider than the box it drops from, which is what
            // makes the preview text worth having at all; the box itself shows
            // the same row cut to fit.
            SelectionBoxItemTemplate = new FuncDataTemplate<PassageEntry>((entry, _) => BuildEntry(entry, double.NaN), supportsRecycling: false),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(_gotoBox, MacKeys ? "Go to a passage (⌘L)" : "Go to a passage (Ctrl+L)");
        _gotoBox.SelectionChanged += async (_, _) => await JumpToSelectedAsync();

        var gotoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        gotoRow.Children.Add(new TextBlock { Text = "Go to", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });
        gotoRow.Children.Add(_gotoBox);

        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_headerLabel);
        titles.Children.Add(_progressLabel);

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(gotoRow, 1);
        headerGrid.Children.Add(titles);
        headerGrid.Children.Add(gotoRow);

        var header = new Border { Child = headerGrid, Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("Cc.PageHeader"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Cc.Rule"));

        // --- The original: the passage, its words, and what each one is. ---

        // The passage either side is shown dimmed around the current one.
        // Verse citations cut across sentences constantly - a relative pronoun
        // at the end of one passage resolves in the next - and translating a
        // clause with both halves off screen is how you produce something
        // confidently wrong. The neighbours are not clickable and not
        // translated here; they are there to finish a sentence the citation
        // scheme cut in half, which in verse is the ordinary case.
        _previousPassage = PassageText(dim: true);
        _currentPassage = PassageText(dim: false);
        _nextPassage = PassageText(dim: true);
        _passageStack = new StackPanel { Spacing = 14, Margin = new Thickness(18, 14), FlowDirection = sourceDirection };
        _passageStack.Children.Add(_previousPassage);
        _passageStack.Children.Add(_currentPassage);
        _passageStack.Children.Add(_nextPassage);
        _sourceScroller = new ScrollViewer { Content = _passageStack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var sourcePage = new Border { Classes = { "page" }, Child = _sourceScroller };

        // The passage twice over: as running text to read, and as a list of
        // its own words to click. Word hit-testing inside wrapped text is
        // fiddly, and this keeps the passage readable as a passage - and the
        // list can be walked with the arrow keys, which is how a sentence is
        // read word by word.
        _sourceWords = new ListBox
        {
            FontFamily = _sourceFamily,
            FontSize = WordListSize,
            FlowDirection = sourceDirection,
            Padding = new Thickness(4, 6),
            ItemTemplate = new FuncDataTemplate<WordEntry>((word, _) => new TextBlock { Text = word?.Word, Margin = new Thickness(8, 1) })
        };
        _sourceWords.SelectionChanged += async (_, _) => await ShowWordAsync();
        var wordsPage = new Border { Classes = { "page" }, Child = _sourceWords };

        _reference = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(16, 12)
        };
        var referencePage = new Border { Classes = { "page" }, Child = _reference };

        // Opens Word Study on this passage. Its occurrences start narrowed to
        // this work: "how does this author use it here" is answerable, where
        // the corpus-wide count for a common word stops at the result limit
        // and says only that the word is common.
        var wordStudyButton = SmallButton("WordStudy", "Word Study");
        wordStudyButton.Click += (_, _) =>
        {
            if (_sourcePassages.Count == 0) return;

            var label = EditionLabels.Descriptor(_source);
            WordStudyFeature.Open(_host, new PassageContext(
                _sourcePassages[_index], _source, null, FromOriginal: true, _sourceFamily.Name, label, null));
        };

        // The same lookups as clicking a word, for every word at once and in
        // the order they appear.
        //
        // Clicking word by word tells you about a word. This tells you about
        // a sentence: seeing accusative, then a third-person verb, then a
        // preposition governing a dative, laid out in sequence, is what lets
        // someone assemble a first attempt at all. Without it the only route
        // from "stuck" to "written" is the AI button, and then you are
        // transcribing rather than translating.
        var cribButton = SmallButton("Morphology", "Whole Passage");
        cribButton.Click += async (_, _) => await BuildCribAsync();

        // With the passage rather than with the AI buttons because it belongs
        // to reading the passage, not to translating it - and it is the one
        // thing here that is useful before you can do either.
        var alphabetButton = SmallButton("Alphabet", "Alphabet");
        alphabetButton.Click += (_, _) => AlphabetFeature.Open(_host, _sourceLanguage);

        var passageTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        passageTools.Children.Add(wordStudyButton);
        passageTools.Children.Add(cribButton);
        passageTools.Children.Add(alphabetButton);

        var passageLabel = Section("PASSAGE");
        passageLabel.VerticalAlignment = VerticalAlignment.Bottom;

        var passageHeader = new DockPanel { Margin = new Thickness(2, 0, 0, 6) };
        DockPanel.SetDock(passageTools, Dock.Right);
        passageHeader.Children.Add(passageTools);
        passageHeader.Children.Add(passageLabel);

        var wordsHeader = Section("CLICK A WORD");
        var referenceHeader = Section("HEADWORD, GRAMMAR AND DICTIONARY");

        var lookupGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("210,12,*"),
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        wordsHeader.Margin = new Thickness(2, 0, 0, 6);
        referenceHeader.Margin = new Thickness(2, 0, 0, 6);
        Grid.SetColumn(referenceHeader, 2);
        Grid.SetRow(wordsPage, 1);
        Grid.SetRow(referencePage, 1);
        Grid.SetColumn(referencePage, 2);
        lookupGrid.Children.Add(wordsHeader);
        lookupGrid.Children.Add(referenceHeader);
        lookupGrid.Children.Add(wordsPage);
        lookupGrid.Children.Add(referencePage);

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,2*,16,3*") };
        Grid.SetRow(sourcePage, 1);
        Grid.SetRow(lookupGrid, 3);
        left.Children.Add(passageHeader);
        left.Children.Add(sourcePage);
        left.Children.Add(lookupGrid);

        // --- Your translation, with what you wrote either side. ---

        // Your own rendering of the passage either side, laid out to mirror
        // the original so the eye can travel straight across. Read-only and
        // apart from the box you are writing in, rather than dimmed text in
        // it: the point is that they cannot be edited by accident, and one
        // box would leave the boundary between what you are writing and what
        // you already wrote as a matter of noticing a colour.
        //
        // Worth more than the symmetry suggests. What goes wrong in a
        // translation made over weeks is rarely a mistranslated word - it is
        // rendering the same word "good things" here and "the good" three
        // passages later. Nothing else in the workbench would show you that.
        _previousTranslation = NeighbourText();
        _nextTranslation = NeighbourText();

        _myTranslation = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = _translationFamily,
            FontSize = MyTranslationSize,
            Padding = new Thickness(14, 12),
            VerticalContentAlignment = VerticalAlignment.Top,
            PlaceholderText = "Your translation of this passage"
        };
        _myTranslation.TextChanged += (_, _) => RefreshRevealButton();

        var myHeader = new DockPanel { Margin = new Thickness(2, 0, 0, 6) };
        var myCaption = new TextBlock { Text = "before and after shown greyed", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Bottom };
        DockPanel.SetDock(myCaption, Dock.Right);
        myHeader.Children.Add(myCaption);
        myHeader.Children.Add(Section("YOUR TRANSLATION"));

        // Which translation to check against, when the work has more than
        // one. Pre-selected by citation-ref overlap with the source edition,
        // which is inference rather than recorded fact - hence a picker
        // rather than a silent choice.
        var comparisonItems = new List<string> { comparisons.Count == 0 ? "(no other translation loaded)" : "(none)" };
        comparisonItems.AddRange(comparisons.Select(c => c.Label));
        _comparisonBox = new ComboBox
        {
            ItemsSource = comparisonItems,
            SelectedIndex = comparisons.Count > 0 ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _comparisonBox.SelectionChanged += async (_, _) => await ChangeComparisonAsync();

        var comparisonRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var comparisonLabel = new TextBlock { Text = "Check against:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(comparisonLabel, Dock.Left);
        comparisonRow.Children.Add(comparisonLabel);
        comparisonRow.Children.Add(_comparisonBox);

        // Named for what they do rather than for being helpful. Everywhere
        // else in the app an AI request says so before it is made, and these
        // two send text over the internet like any other.
        _glossWordButton = SmallButton("Translate", "AI Translate Word");
        _glossWordButton.IsEnabled = false;
        _glossWordButton.Click += async (_, _) => await GlossWordAsync();

        _suggestButton = SmallButton("Translate", "AI Translate Passage");
        _suggestButton.Click += async (_, _) => await SuggestAsync();

        // The same glyph Compare Translations uses - it is the same act,
        // against one passage instead of a whole work.
        _revealButton = SmallButton("CompareTranslations", "Compare published");
        _revealButton.IsEnabled = false;
        _revealButton.MinWidth = 190;
        _revealButton.Click += (_, _) => RevealPublished();

        var aiRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var button in new[] { _glossWordButton, _suggestButton, _revealButton })
        {
            button.Margin = new Thickness(0, 0, 8, 0);
            aiRow.Children.Add(button);
        }

        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto") };
        _previousBox = NeighbourBox(_previousTranslation);
        _previousBox.Margin = new Thickness(0, 0, 0, 8);
        _nextBox = NeighbourBox(_nextTranslation);
        _nextBox.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_previousBox, 1);
        Grid.SetRow(_myTranslation, 2);
        Grid.SetRow(_nextBox, 3);
        Grid.SetRow(comparisonRow, 4);
        Grid.SetRow(aiRow, 5);
        right.Children.Add(myHeader);
        right.Children.Add(_previousBox);
        right.Children.Add(_myTranslation);
        right.Children.Add(_nextBox);
        right.Children.Add(comparisonRow);
        right.Children.Add(aiRow);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("11*,16,9*"), Margin = new Thickness(20, 16, 20, 14) };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        body.Children.Add(left);
        body.Children.Add(splitter);
        body.Children.Add(right);

        // --- The loop this window exists for: write, save, move on. ---

        _previousButton = new Button { Content = new IconLabel("Back", "Previous"), MinWidth = 104 };
        ToolTip.SetTip(_previousButton, MacKeys ? "Save and go back (⌘[)" : "Save and go back (Ctrl+[)");
        _previousButton.Click += async (_, _) => await MoveAsync(-1);

        _nextButton = new Button { Classes = { "accent" }, MinWidth = 150 };
        ToolTip.SetTip(_nextButton, MacKeys ? "Save and move on (⌘↩ or ⌘])" : "Save and move on (Ctrl+Enter or Ctrl+])");
        _nextButton.Click += async (_, _) => await MoveAsync(1);

        var closeButton = new Button { Content = "Save and Close", MinWidth = 120 };
        closeButton.Click += (_, _) => Close();

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        navigation.Children.Add(_previousButton);
        navigation.Children.Add(_nextButton);
        navigation.Children.Add(closeButton);

        var keys = new TextBlock
        {
            Text = MacKeys ? "⌘↩ save and next   ⌘[ ⌘] move   ⌘S save   ⌘L go to" : "Ctrl+Enter save and next   Ctrl+[ ] move   Ctrl+S save   Ctrl+L go to",
            Classes = { "faint" },
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        _statusLabel = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0) };

        var footerDock = new DockPanel();
        DockPanel.SetDock(navigation, Dock.Left);
        DockPanel.SetDock(keys, Dock.Right);
        footerDock.Children.Add(navigation);
        footerDock.Children.Add(keys);
        footerDock.Children.Add(_statusLabel);

        var footer = new Border { Child = footerDock, Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("Cc.Rule"));

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(body);

        // Escape stays with the text box rather than closing the window: it
        // is where the hands are, and a half-written passage should not be
        // one stray key from the window going away. ⌘W still closes, and
        // saves first.
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) e.Handled = true;
        };

        Content = root;

        // Tunnelling, so the chords reach the window before the translation
        // box - a multiline box would otherwise take ⌘Return as a new line.
        // None of them is an unmodified key: Return has to keep making a new
        // line in the box.
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);

        // Opens at the first passage with nothing written yet, which for
        // anyone returning to a long work is where they stopped rather than
        // where they started.
        _index = FirstUntranslatedIndex();

        Opened += async (_, _) =>
        {
            await ChangeComparisonAsync();
            _myTranslation.Focus();
        };

        // The size can be changed from the main window while this one is
        // open, so it is followed rather than read once.
        ReadingFontSettings.Changed += ApplyReadingFontSize;
        Closed += (_, _) => ReadingFontSettings.Changed -= ApplyReadingFontSize;

        ShowPassage();
    }

    /// <summary>The edition being written, which the window was opened on.</summary>
    public Edition Translation => _translation;

    public int WorkId => _work.WorkId;

    internal TextBox TranslationBox => _myTranslation;

    internal ListBox WordList => _sourceWords;

    internal int Index => _index;

    internal string HeaderText => _headerLabel.Text ?? string.Empty;

    internal string ProgressText => _progressLabel.Text ?? string.Empty;

    internal string StatusText => _statusLabel.Text ?? string.Empty;

    internal string ReferenceText => _referenceText;

    internal string RevealButtonText => (_revealButton.Content as IconLabel)?.Text ?? string.Empty;

    internal bool RevealEnabled => _revealButton.IsEnabled;

    internal string PreviousTranslationText => _previousTranslation.Text ?? string.Empty;

    // --- Building blocks. ---

    private static TextBlock Section(string text) => new() { Text = text, Classes = { "section" } };

    private static Button SmallButton(string glyph, string text) =>
        new() { Content = new IconLabel(glyph, text), Padding = new Thickness(10, 4), FontSize = 12.5 };

    private SelectableTextBlock PassageText(bool dim)
    {
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = _sourceFamily };
        if (dim) text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkFaint"));
        return text;
    }

    private SelectableTextBlock NeighbourText()
    {
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = _translationFamily,
            FontSize = NeighbourTranslationSize,
            FontStyle = FontStyle.Normal
        };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
        return text;
    }

    private static Border NeighbourBox(Control content) => new()
    {
        Classes = { "panel" },
        Padding = new Thickness(14, 8),
        MinHeight = 40,
        Child = new ScrollViewer
        {
            Content = content,
            MaxHeight = 96,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        }
    };

    private Control BuildEntry(PassageEntry? entry, double width)
    {
        var mark = new TextBlock { Width = 18, VerticalAlignment = VerticalAlignment.Center };
        mark.Bind(TextBlock.ForegroundProperty, mark.GetResourceObservable("Cc.Accent"));
        // A column in the list, so the previews line up; in the closed box,
        // just as wide as the reference it holds.
        var citation = new TextBlock
        {
            Width = double.IsNaN(width) ? double.NaN : 96,
            Margin = new Thickness(0, 0, 10, 0),
            Classes = { "caption" },
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var preview = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = _sourceFamily, VerticalAlignment = VerticalAlignment.Center };

        void Show()
        {
            mark.Text = entry?.Done == true ? "✓" : "·";
            citation.Text = entry?.Citation;
            preview.Text = entry?.Preview;
        }

        Show();

        // Only while the row is on screen, as the reader pane's rows do: the
        // entries outlive their visuals, and a handler left behind would keep
        // every dead visual alive.
        PropertyChangedEventHandler onChanged = (_, _) => Show();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Width = width };
        grid.AttachedToVisualTree += (_, _) =>
        {
            if (entry == null) return;
            entry.PropertyChanged -= onChanged;
            entry.PropertyChanged += onChanged;
            Show();
        };
        grid.DetachedFromVisualTree += (_, _) =>
        {
            if (entry != null) entry.PropertyChanged -= onChanged;
        };

        Grid.SetColumn(citation, 1);
        Grid.SetColumn(preview, 2);
        grid.Children.Add(mark);
        grid.Children.Add(citation);
        grid.Children.Add(preview);
        return grid;
    }

    /// <summary>
    /// How a passage reads in the Go to list - its citation reference with
    /// the shared prefix removed, a tick once it has been translated, and
    /// enough of the text to recognise it by.
    /// </summary>
    private static string Preview(string text) => text.Length > 60 ? text[..60] + "…" : text;

    private string ShortCitation(string citationRef) =>
        _citationPrefix.Length > 0 && citationRef.StartsWith(_citationPrefix, StringComparison.Ordinal)
            ? citationRef[_citationPrefix.Length..]
            : citationRef;

    // --- Reading. ---

    /// <summary>Follows a change of reading size made in the main window.</summary>
    private void ApplyReadingFontSize()
    {
        _sourceWords.FontSize = WordListSize;
        _myTranslation.FontSize = MyTranslationSize;
        _previousTranslation.FontSize = NeighbourTranslationSize;
        _nextTranslation.FontSize = NeighbourTranslationSize;

        if (_sourcePassages.Count > 0) RenderPassageWithContext();
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!command) return;

        switch (e.Key)
        {
            // ⌘Return saves and advances: the same gesture for "done, send"
            // that most applications use. ⌘[ and ⌘] match the reader's own
            // back and forward - and leave ⌥←/→, which move by a word in a
            // Mac text box, to the text box.
            case Key.Enter:
            case Key.OemCloseBrackets:
                _ = MoveAsync(1);
                e.Handled = true;
                break;

            case Key.OemOpenBrackets:
                _ = MoveAsync(-1);
                e.Handled = true;
                break;

            case Key.S:
                _ = SaveCurrentAsync();
                e.Handled = true;
                break;

            case Key.L:
                _gotoBox.Focus();
                _gotoBox.IsDropDownOpen = true;
                e.Handled = true;
                break;
        }
    }

    private async Task ChangeComparisonAsync()
    {
        var choice = _comparisonBox.SelectedIndex - 1;

        try
        {
            _publishedAligner = choice >= 0 && choice < _comparisons.Count
                ? await _loadAligner(_comparisons[choice].Edition.EditionId)
                : null;
        }
        catch (Exception ex)
        {
            _publishedAligner = null;
            _statusLabel.Text = $"Couldn't read that translation: {ex.Message}";
        }

        _revealed = false;
        RefreshRevealButton();
    }

    private int FirstUntranslatedIndex()
    {
        for (var i = 0; i < _sourcePassages.Count; i++)
        {
            if (!_myTranslations.ContainsKey(_sourcePassages[i].CitationRef)) return i;
        }

        return 0;
    }

    private void ShowPassage()
    {
        if (_sourcePassages.Count == 0)
        {
            _headerLabel.Text = "This work has no original-language text loaded.";
            _nextButton.Content = "Save";
            return;
        }

        _index = Math.Clamp(_index, 0, _sourcePassages.Count - 1);
        var passage = _sourcePassages[_index];

        _headerLabel.Text = $"{_authorName}, {_work.Title}  [{PassageCitation.Display(passage.CitationRef, passage.Milestone)}]";
        RenderPassageWithContext();

        _sourceWords.ItemsSource = PassageCrib.SplitWords(passage.Text).Select((w, i) => new WordEntry(i, w)).ToList();

        _myTranslation.Text = _myTranslations.TryGetValue(passage.CitationRef, out var mine) ? mine : string.Empty;

        _previousTranslation.Text = NeighbouringTranslation(_index - 1);
        _nextTranslation.Text = NeighbouringTranslation(_index + 1);
        _previousBox.IsVisible = _index > 0;
        _nextBox.IsVisible = _index < _sourcePassages.Count - 1;

        ShowReferenceText("Click a word on the left.");
        _glossWordButton.IsEnabled = false;

        _revealed = false;
        RefreshRevealButton();

        _progressLabel.Text = $"Passage {_index + 1:N0} of {_sourcePassages.Count:N0}   ·   {_myTranslations.Count:N0} translated";

        _navigating = true;
        try
        {
            if (_index < _entries.Count) _gotoBox.SelectedIndex = _index;
        }
        finally
        {
            _navigating = false;
        }

        _previousButton.IsEnabled = _index > 0;
        _nextButton.Content = _index < _sourcePassages.Count - 1 ? "Save and Next ▶" : "Save";
    }

    /// <summary>
    /// What you wrote for the passage at this position, or a note that you
    /// haven't yet. The empty case says so rather than showing a blank box,
    /// which would read as something having failed to load.
    /// </summary>
    private string NeighbouringTranslation(int index)
    {
        if (index < 0 || index >= _sourcePassages.Count) return string.Empty;

        var citation = _sourcePassages[index].CitationRef;

        return _myTranslations.TryGetValue(citation, out var text) && text.Length > 0
            ? text
            : $"[{ShortCitation(citation)} - not translated yet]";
    }

    /// <summary>Draws the passage with the one before and after it dimmed around it.</summary>
    private void RenderPassageWithContext()
    {
        var hasPrevious = _index > 0;
        var hasNext = _index < _sourcePassages.Count - 1;

        _previousPassage.IsVisible = hasPrevious;
        _previousPassage.Text = hasPrevious ? _sourcePassages[_index - 1].Text : string.Empty;
        _previousPassage.FontSize = ContextSize;
        _previousPassage.LineHeight = ContextSize * 1.4;

        _currentPassage.Text = _sourcePassages[_index].Text;
        _currentPassage.FontSize = WorkingSize;
        _currentPassage.LineHeight = WorkingSize * 1.4;

        _nextPassage.IsVisible = hasNext;
        _nextPassage.Text = hasNext ? _sourcePassages[_index + 1].Text : string.Empty;
        _nextPassage.FontSize = ContextSize;
        _nextPassage.LineHeight = ContextSize * 1.4;

        // Scrolled so the passage being worked on is what you see, not
        // whatever preceded it - with the end of the one before still in
        // sight above it.
        Dispatcher.UIThread.Post(() =>
        {
            var top = _currentPassage.TranslatePoint(new Point(0, 0), _passageStack);
            if (top == null) return;
            _sourceScroller.Offset = new Vector(0, Math.Max(0, top.Value.Y - ContextSize * 2.2));
        }, DispatcherPriority.Background);
    }

    // --- The reference panel. Everything that is not your translation goes here. ---

    private void ShowReferenceText(string text)
    {
        _referenceText = text;
        _reference.Content = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = _sourceFamily,
            FontSize = 14.5,
            LineHeight = 21
        };
    }

    /// <summary>Text with some runs set apart: a headword in bold, a caveat muted.</summary>
    private sealed class RichText
    {
        private readonly SelectableTextBlock _block;
        private readonly StringBuilder _plain = new();

        public RichText(FontFamily font)
        {
            _block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = font, FontSize = 14.5, LineHeight = 21 };
            _block.Inlines = new InlineCollection();
        }

        public RichText Add(string text, string? brush = null, FontWeight weight = FontWeight.Normal, double? size = null, FontStyle style = FontStyle.Normal)
        {
            var run = new Run(text) { FontWeight = weight, FontStyle = style };
            if (size != null) run.FontSize = size.Value;
            if (brush != null) run.Bind(TextElement.ForegroundProperty, _block.GetResourceObservable(brush));
            _block.Inlines!.Add(run);
            _plain.Append(text);
            return this;
        }

        public RichText Line()
        {
            _block.Inlines!.Add(new LineBreak());
            _plain.AppendLine();
            return this;
        }

        public SelectableTextBlock Block => _block;

        public string Plain => _plain.ToString();
    }

    private void ShowReference(RichText text)
    {
        _referenceText = text.Plain;
        _reference.Content = text.Block;
    }

    /// <summary>
    /// Every word of the passage with its headword, parse and a short gloss,
    /// in the order they appear.
    ///
    /// Repeated words are looked up once - a passage of forty words with the
    /// article in it several times shouldn't mean several identical queries.
    /// </summary>
    internal async Task BuildCribAsync()
    {
        if (_sourcePassages.Count == 0) return;

        var generation = ++_lookupGeneration;
        ShowReferenceText("Reading the passage...");

        try
        {
            var words = PassageCrib.SplitWords(_sourcePassages[_index].Text).ToList();

            var lookups = new Dictionary<string, List<(string Headword, string? PartOfSpeech)>>();
            foreach (var word in words)
            {
                if (lookups.ContainsKey(word)) continue;
                lookups[word] = PassageCrib.Rank(await _lemmaRepo.GetHeadwordsForFormAsync(word, _sourceLanguage), word);
            }

            if (generation != _lookupGeneration) return;

            ShowCrib(PassageCrib.Build(words, lookups));
        }
        catch (Exception ex)
        {
            if (generation == _lookupGeneration) ShowReferenceText($"Couldn't read the passage: {ex.Message}");
        }
    }

    /// <summary>
    /// The crib as two columns - the word as written, and what it is - so the
    /// sentence can be read down the left and understood down the right.
    /// </summary>
    private void ShowCrib(List<CribLine> lines)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*") };
        var plain = new StringBuilder();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var word = new SelectableTextBlock
            {
                Text = line.Word,
                FontFamily = _sourceFamily,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 3)
            };
            Grid.SetRow(word, i);

            var reading = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = _sourceFamily, FontSize = 14.5, Margin = new Thickness(0, 3) };
            reading.Inlines = new InlineCollection();
            Grid.SetRow(reading, i);
            Grid.SetColumn(reading, 2);

            if (line.Headword == null)
            {
                var none = new Run("—");
                none.Bind(TextElement.ForegroundProperty, reading.GetResourceObservable("Cc.InkFaint"));
                reading.Inlines.Add(none);
                plain.AppendLine($"{line.Word,-16} —");
            }
            else
            {
                reading.Inlines.Add(new Run(line.Headword));
                var text = $"{line.Word,-16} {line.Headword}";

                if (line.Grammar.Length > 0)
                {
                    var grammar = new Run($"  ({line.Grammar})") { FontSize = 13 };
                    grammar.Bind(TextElement.ForegroundProperty, reading.GetResourceObservable("Cc.InkMuted"));
                    reading.Inlines.Add(grammar);
                    text += $"  ({line.Grammar})";
                }

                if (line.Alternatives.Count > 0)
                {
                    reading.Inlines.Add(new LineBreak());
                    var or = new Run($"or: {string.Join(", ", line.Alternatives)}") { FontSize = 12.5 };
                    or.Bind(TextElement.ForegroundProperty, reading.GetResourceObservable("Cc.InkFaint"));
                    reading.Inlines.Add(or);
                    text += $"{Environment.NewLine}                 or: {string.Join(", ", line.Alternatives)}";
                }

                plain.AppendLine(text);
            }

            grid.Children.Add(word);
            grid.Children.Add(reading);
        }

        var hint = new TextBlock
        {
            Text = "Click any word on the left for its full dictionary entry.",
            Classes = { "caption" },
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(hint, lines.Count);
        Grid.SetColumnSpan(hint, 3);
        grid.Children.Add(hint);

        plain.AppendLine();
        plain.AppendLine(hint.Text);

        _referenceText = plain.ToString();
        _reference.Content = grid;
        _reference.Offset = default;
    }

    internal async Task ShowWordAsync()
    {
        if (_sourceWords.SelectedItem is not WordEntry selected)
        {
            _glossWordButton.IsEnabled = false;
            return;
        }

        var word = selected.Word;
        var generation = ++_lookupGeneration;

        _glossWordButton.IsEnabled = true;
        ShowReferenceText("Looking up...");

        try
        {
            var headwords = PassageCrib.Rank(await _lemmaRepo.GetHeadwordsForFormAsync(word, _sourceLanguage), word);
            if (generation != _lookupGeneration) return;

            if (headwords.Count == 0)
            {
                ShowReferenceText(
                    $"{word}\n\nNo headword found for this form. That usually means the lemma " +
                    "data for this language isn't loaded (Setup), or the form is one the " +
                    "lemma data doesn't cover - proper names especially.");
                return;
            }

            var report = new RichText(_sourceFamily);
            report.Add(word, weight: FontWeight.SemiBold, size: 19).Line().Line();

            foreach (var (headword, tag) in headwords)
            {
                report.Add(headword, weight: FontWeight.SemiBold, size: 16);

                var parse = MorphologyDecoder.Decode(tag);
                if (parse.IsDecoded && parse.Description.Length > 0)
                {
                    report.Line().Add(parse.Description, "Cc.InkMuted", size: 13);
                }

                if (!string.IsNullOrWhiteSpace(_sourceLanguage))
                {
                    var entries = await _definitionRepo.GetByHeadwordAsync(headword, _sourceLanguage);
                    if (generation != _lookupGeneration) return;

                    foreach (var (_, entry, source) in entries.Take(2))
                    {
                        report.Line().Line().Add(entry);
                        if (!string.IsNullOrWhiteSpace(source)) report.Line().Add($"- {source}", "Cc.InkFaint", size: 12.5);
                    }
                }

                report.Line().Line();
            }

            ShowReference(report);
            _reference.Offset = default;
        }
        catch (Exception ex)
        {
            if (generation == _lookupGeneration) ShowReferenceText($"Couldn't look that up: {ex.Message}");
        }
    }

    // --- Checking against the published translation. ---

    /// <summary>
    /// The published translation stays out of reach until something has been
    /// written.
    ///
    /// Not paternalism for its own sake - reading someone else's rendering
    /// first is the one action that quietly removes the point of doing this,
    /// and the gate costs nothing to anyone using this purely to author,
    /// since they have typed by the time they would want to compare.
    /// </summary>
    private void RefreshRevealButton()
    {
        var hasAttempt = (_myTranslation.Text ?? string.Empty).Trim().Length > 0;

        _revealButton.IsEnabled = _publishedAligner != null && hasAttempt && !_revealed;

        // The disabled state has two quite different causes and it is worth
        // saying which: nothing to compare against, versus deliberately
        // withheld until something has been attempted.
        ((IconLabel)_revealButton.Content!).Text =
            _publishedAligner == null ? "Nothing to compare"
            : _revealed ? "Comparing"
            : hasAttempt ? "Compare published"
            : "Compare (write first)";
    }

    internal void RevealPublished()
    {
        if (_publishedAligner == null || _sourcePassages.Count == 0) return;

        var passage = _sourcePassages[_index];
        var published = _publishedAligner.ResolveText(passage.CitationRef);

        if (string.IsNullOrWhiteSpace(published))
        {
            _statusLabel.Text = "The published translation has nothing at this citation.";
            return;
        }

        _revealed = true;
        RefreshRevealButton();

        var report = new RichText(_translationFamily);
        report.Add($"Published translation of [{PassageCitation.Display(passage.CitationRef, passage.Milestone)}]", weight: FontWeight.SemiBold)
            .Line().Line()
            .Add(published);
        ShowReference(report);
    }

    // --- The AI, beside your work and never in it. ---

    /// <summary>
    /// Asks what the selected word means in this line.
    ///
    /// Deliberately narrower than the dictionary panel rather than a
    /// replacement for it: the lexicon entry is sourced and complete, while
    /// this answers the one thing it can't - which sense is in play here, and
    /// what the word is doing in this sentence.
    /// </summary>
    private async Task GlossWordAsync()
    {
        if (_sourceWords.SelectedItem is not WordEntry selected || _sourcePassages.Count == 0) return;

        var word = selected.Word;
        var geminiKey = TranslationSettings.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            await NoGeminiKeyAsync("AI Translate Word");
            return;
        }

        var passage = _sourcePassages[_index];
        if (!await ConfirmSendAsync($"the word “{word}” and the line it appears in", passage.Text.Length)) return;

        _glossWordButton.IsEnabled = false;
        _statusLabel.Text = $"Asking about {word}...";

        try
        {
            var gloss = await GeminiTranslationService.GlossWordAsync(
                word, passage.Text, _sourceLanguage, "eng", _authorName, _work.Title, passage.CitationRef, geminiKey);

            var report = new RichText(_sourceFamily);
            report.Add($"{word} - as used here", weight: FontWeight.SemiBold).Line().Line()
                .Add(gloss).Line().Line()
                .Add("(AI-generated, unlike the dictionary entry - worth checking against it.)", "Cc.InkMuted", style: FontStyle.Italic);
            ShowReference(report);
            _statusLabel.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't ask about that word: {ex.Message}";
        }
        finally
        {
            _glossWordButton.IsEnabled = _sourceWords.SelectedItem is WordEntry;
        }
    }

    private async Task SuggestAsync()
    {
        if (_sourcePassages.Count == 0) return;

        var geminiKey = TranslationSettings.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            await NoGeminiKeyAsync("AI Translate Passage");
            return;
        }

        var passage = _sourcePassages[_index];
        var citation = PassageCitation.Display(passage.CitationRef, passage.Milestone);
        if (!await ConfirmSendAsync($"this passage, [{citation}]", passage.Text.Length)) return;

        _suggestButton.IsEnabled = false;
        _statusLabel.Text = "AI translating this passage...";

        try
        {
            var suggestion = await GeminiTranslationService.TranslateAsync(
                passage.Text, _sourceLanguage, "eng", _authorName, _work.Title, passage.CitationRef, geminiKey);

            // Into the reference panel, never into the box. A suggestion
            // that arrives already typed stops being something to weigh
            // against and becomes the answer.
            var report = new RichText(_translationFamily);
            report.Add($"AI translation of [{citation}]", weight: FontWeight.SemiBold).Line().Line()
                .Add(suggestion).Line().Line()
                .Add("(AI-generated - a rendering to weigh against yours, not a correct answer.)", "Cc.InkMuted", style: FontStyle.Italic);
            ShowReference(report);
            _statusLabel.Text = "AI translation shown on the left - yours stays as you wrote it.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't get an AI translation: {ex.Message}";
        }
        finally
        {
            _suggestButton.IsEnabled = true;
        }
    }

    private async Task NoGeminiKeyAsync(string title)
    {
        var choice = await Alert.Show(this, title,
            "No Gemini key is configured. Add one in Tools → AI Translation Settings.",
            AlertKind.Info, "OK", "AI Translation Settings…");

        if (choice == 1) await SettingsFeature.OpenAiSettingsAsync(this);
    }

    /// <summary>
    /// The confirmation the Windows workbench was once missing. Every other
    /// place in the application that reaches the network asks first and
    /// honours the always-confirm setting; these two went straight from a
    /// button press to Gemini, which made the setting a promise the
    /// application did not keep - and this is the surface where the text
    /// being sent is someone's unpublished transcription of a manuscript.
    /// </summary>
    private async Task<bool> ConfirmSendAsync(string what, int characters)
    {
        if (!TranslationSettings.AlwaysConfirmBeforeSending) return true;

        return await Alert.Confirm(this, "Send to Gemini's API?",
            $"This will send {what} (about {characters:N0} characters) to Gemini's API over the " +
            "internet. This is the one thing in Classica Codex that isn't offline.\n\n" +
            "Continue? (You can turn this confirmation off in AI Translation Settings.)",
            "Send", "Cancel", AlertKind.Warning);
    }

    // --- Saving and moving. ---

    internal async Task SaveCurrentAsync()
    {
        if (_sourcePassages.Count == 0) return;

        var index = _index;
        var passage = _sourcePassages[index];
        var text = (_myTranslation.Text ?? string.Empty).Trim();

        try
        {
            var changes = await _textNodeRepo.SaveTranslatedLineAsync(_translation.EditionId, passage.CitationRef, index, text);

            // A line you write here is a line you will want to find again.
            //
            // Whole-word search uses the word index, so a saved passage that
            // never reached it was visible in the reader and could not be
            // searched for. Guarded the same way Create Translation guards
            // it: only when an index already exists. Writing rows into an
            // empty index would make every search believe the library is
            // indexed when the only thing in it is this translation - see
            // CreateTranslationWindow, which explains that trap at length.
            if (changes.Any && await _wordIndexRepo.HasDataAsync())
            {
                await _wordIndexService.ApplyChangesAsync(changes);
            }

            if (text.Length == 0) _myTranslations.Remove(passage.CitationRef);
            else _myTranslations[passage.CitationRef] = text;

            // Only this passage's entry is touched - rebuilding the whole
            // list on every save would mean a thousand-odd rows each time
            // Next is pressed.
            if (index < _entries.Count) _entries[index].Done = text.Length > 0;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't save this passage: {ex.Message}";
        }
    }

    internal async Task MoveAsync(int delta)
    {
        await SaveCurrentAsync();

        var next = _index + delta;
        if (next < 0 || next >= _sourcePassages.Count)
        {
            _statusLabel.Text = delta > 0 ? "That was the last passage." : "Already at the first passage.";
            ShowPassage();
            return;
        }

        _index = next;
        _statusLabel.Text = string.Empty;
        ShowPassage();
        _myTranslation.Focus();
        _myTranslation.CaretIndex = _myTranslation.Text?.Length ?? 0;
    }

    private async Task JumpToSelectedAsync()
    {
        if (_navigating) return;

        var target = _gotoBox.SelectedIndex;
        if (target < 0 || target >= _sourcePassages.Count || target == _index) return;

        await SaveCurrentAsync();
        _index = target;
        _statusLabel.Text = string.Empty;
        ShowPassage();
    }

    /// <summary>
    /// Saves the passage in hand before the window goes, so the reader that
    /// reloads behind it sees what was just written. When the reader itself
    /// or the app is closing, the save is begun and the window goes with it,
    /// rather than holding up the quit.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeSaved) return;

        _closeSaved = true;

        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                await SaveCurrentAsync();
                Close();
            });
            return;
        }

        _ = SaveCurrentAsync();
    }

    /// <summary>A word in the list. Distinct objects, because the list keeps every repetition on purpose.</summary>
    private sealed record WordEntry(int Position, string Word);

    /// <summary>A passage in the Go to list, whose tick changes as it is translated.</summary>
    private sealed class PassageEntry : INotifyPropertyChanged
    {
        private bool _done;

        public PassageEntry(int index, string citation, string preview, bool done)
        {
            Index = index;
            Citation = citation;
            Preview = preview;
            _done = done;
        }

        public int Index { get; }

        public string Citation { get; }

        public string Preview { get; }

        public bool Done
        {
            get => _done;
            set
            {
                if (_done == value) return;
                _done = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Done)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
