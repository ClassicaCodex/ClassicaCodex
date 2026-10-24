using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Catmus;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.MedievalHands;

/// <summary>
/// Medieval hands: 313 manuscripts from CATMuS-Medieval, each line as the
/// scribe wrote it beside a diplomatic transcription of it - the Windows
/// PalaeographyForm, filter for filter.
///
/// <b>Its own window rather than the reader, because this is not text.</b>
/// CATMuS shuffles its rows and records no page or line number, so the lines
/// of a manuscript cannot be put back in order - see the schema comment on
/// CatmusLines. Loading them into the library as an edition would produce a
/// work whose every line was genuine and whose order was invented, and it
/// would then be read, cited and bookmarked as though the order meant
/// something. Here they are specimens, which is what they are: what a
/// twelfth-century Gothic documentary hand looks like, what ꝯ and ꝑ and ꝰ
/// look like when a scribe actually writes them, and what a transcriber made
/// of it.
///
/// Which is the thing the rest of this application cannot show. It holds a
/// great deal of medieval text - Menota, the Reference Corpus of Middle High
/// German, the Old French epics - and every word of it arrives already
/// transcribed, with the step where someone looked at a manuscript and
/// decided what it said left out. This window is that step.
///
/// The catalogue is embedded in Core, so the whole of it is listed before a
/// byte has been downloaded; what this library holds is what is not greyed.
/// Every query, file read and decode runs off the UI thread - SQLite's async
/// methods complete synchronously, and a line photograph is a PNG of 4,772
/// pixels - so moving down the lines with the arrow keys never stalls.
/// </summary>
public sealed class MedievalHandsWindow : FeatureWindow
{
    /// <summary>How many of a manuscript's lines are listed. The pane says when there are more.</summary>
    private const int LineLimit = 2000;

    private const int SearchLimit = 500;

    private readonly CatmusRepository _repository = new();
    private readonly Action? _openSetup;

    private readonly ComboBox _languageFilter;
    private readonly ComboBox _centuryFilter;
    private readonly ComboBox _scriptFilter;
    private readonly CheckBox _downloadedOnly;
    private readonly TextBox _searchBox;
    private readonly CheckBox _exactCharacters;
    private readonly Button _searchButton;

    private readonly Border _notInstalledNotice;
    private readonly ListBox _manuscriptList;
    private readonly TextBlock _manuscriptCount;
    private readonly ListBox _lineList;
    private readonly TextBlock _linesTitle;
    private readonly TextBlock _lineCount;
    private readonly StackPanel _linesEmpty;
    private readonly ContentControl _linesEmptyArt;
    private readonly TextBlock _linesEmptyMessage;
    private readonly ManuscriptImageView _specimen;
    private readonly ComboBox _zoom;
    private readonly TextBlock _zoomPercent;
    private readonly SelectableTextBlock _transcription;
    private readonly SelectableTextBlock _description;
    private readonly TextBlock _statusLabel;
    private readonly ProgressBar _progress;
    private readonly Button _stopButton;
    private readonly Button _getTextButton;
    private readonly Button _getImagesButton;
    private readonly Button _pagesButton;

    private readonly FontFamily _medievalFont;
    private readonly Dictionary<string, ManuscriptPagesWindow> _pagesWindows = new(StringComparer.Ordinal);

    private List<ManuscriptRow> _shown = new();
    private List<CatmusLine> _lines = new();
    private Dictionary<string, CatmusHolding> _held = new(StringComparer.Ordinal);

    /// <summary>Whether the lines listed came from a search across the library rather than from one manuscript.</summary>
    private bool _searchResults;

    /// <summary>A line to select once its manuscript's lines have loaded - set by opening a search result's manuscript.</summary>
    private long? _pendingLineId;

    private CancellationTokenSource? _work;

    /// <summary>
    /// Cancels the photograph being read when another line is chosen.
    /// Someone running down the lines with the arrow keys starts a read per
    /// keypress, and without this the one that happens to finish last wins.
    /// </summary>
    private CancellationTokenSource? _imageLoading;

    /// <summary>
    /// Bumped by every change of what the line list holds, so a slow answer
    /// for a manuscript the reader has already moved on from is dropped
    /// rather than written over the one now selected.
    /// </summary>
    private int _linesGeneration;

    private bool _rebuilding;
    private bool _settingZoom;
    private bool _closed;
    private Task _pending = Task.CompletedTask;
    private Task _imageTask = Task.CompletedTask;

    public MedievalHandsWindow(Action? openSetup = null)
    {
        _openSetup = openSetup;
        _medievalFont = TranscriptionFont();

        Title = "Medieval Hands";
        Width = 1180;
        Height = 780;
        MinWidth = 880;
        MinHeight = 580;

        // --- search row ------------------------------------------------
        _searchBox = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Find a word or a sign",
            FontSize = 14,
            MinHeight = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = _medievalFont,
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Search"), Width = 14, Height = 14, Margin = new Thickness(10, 0, 2, 0), Opacity = 0.6 }
        };
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await RunSearchAsync();
        };

        _exactCharacters = new CheckBox { Content = "Exactly as written", VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_exactCharacters,
            "Match the transcription character for character - how to find every line where a scribe wrote " +
            "a particular sign. Otherwise the marks written over letters are folded away, so “nre” finds “nr̃e”.");

        _searchButton = new Button { Content = "Search", Classes = { "accent" }, MinWidth = 92, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        var searchButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(14, 0, 0, 0), Children = { _exactCharacters, _searchButton } };
        var searchRow = new DockPanel();
        DockPanel.SetDock(searchButtons, Dock.Right);
        searchRow.Children.Add(searchButtons);
        searchRow.Children.Add(_searchBox);

        // --- filter row ------------------------------------------------
        // Built from the catalogue, so what is offered is always what is
        // actually there - a filter for a language no manuscript has is worse
        // than no filter.
        _languageFilter = Combo(150, "Any language", CatmusCatalogue.Languages);
        _centuryFilter = Combo(130, "Any century", CatmusCatalogue.Centuries.Select(c => c + OrdinalSuffix(c) + " century"));
        _scriptFilter = Combo(200, "Any script", CatmusCatalogue.ScriptTypes);
        foreach (var combo in new[] { _languageFilter, _centuryFilter, _scriptFilter })
        {
            combo.SelectionChanged += (_, _) => ShowManuscripts();
        }

        _downloadedOnly = new CheckBox { Content = "Downloaded only", VerticalAlignment = VerticalAlignment.Center };
        _downloadedOnly.IsCheckedChanged += (_, _) => ShowManuscripts();

        var filterRow = new WrapPanel { Orientation = Orientation.Horizontal };
        filterRow.Children.Add(Labeled("Language", _languageFilter));
        filterRow.Children.Add(Labeled("Century", _centuryFilter));
        filterRow.Children.Add(Labeled("Script", _scriptFilter));
        filterRow.Children.Add(Spaced(_downloadedOnly));

        // --- the state before any of CATMuS is in the library ------------
        var setupLink = new Button { Classes = { "link" }, Content = "Open Setup…", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 0, 0), IsVisible = openSetup != null };
        setupLink.Click += (_, _) => _openSetup?.Invoke();
        var noticeText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "None of CATMuS is in this library yet. Setup's Medieval Manuscript Hands step fetches every " +
                   "transcription at once - about eight megabytes, and nothing saved to your download folder - or " +
                   "choose a manuscript below and download only its transcriptions. The whole catalogue is listed " +
                   "either way, so you can see what there is first."
        };
        var noticeDock = new DockPanel();
        DockPanel.SetDock(setupLink, Dock.Right);
        noticeDock.Children.Add(setupLink);
        noticeDock.Children.Add(noticeText);
        _notInstalledNotice = new Border { Classes = { "notice" }, Child = noticeDock, Margin = new Thickness(0, 4, 0, 0), IsVisible = false };

        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(searchRow);
        header.Children.Add(filterRow);
        header.Children.Add(_notInstalledNotice);

        // --- manuscripts -----------------------------------------------
        _manuscriptList = CompactList();
        _manuscriptList.ItemTemplate = new FuncDataTemplate<ManuscriptRow?>((row, _) => row == null ? null : BuildManuscriptRow(row), supportsRecycling: false);
        _manuscriptList.SelectionChanged += (_, _) =>
        {
            if (_rebuilding) return;
            _pending = ShowSelectedManuscriptAsync();
        };
        _manuscriptCount = new TextBlock { Classes = { "caption" } };

        // Four columns, as on Windows, with the shelfmark given the room:
        // these run to "Auxerre, Archives départementales de l'Yonne". The
        // line count is in the panel underneath for whichever is selected.
        var manuscriptsBody = WithColumnHeads(_manuscriptList, ManuscriptColumns,
            ("MANUSCRIPT", false), ("CENT.", true), ("SCRIPT", false), ("PHOTOS", true));
        var manuscriptsPane = ToolPane.Build("MANUSCRIPTS", manuscriptsBody, _manuscriptCount);

        // --- lines -----------------------------------------------------
        _lineList = CompactList();
        _lineList.ItemTemplate = new FuncDataTemplate<LineRow?>((row, _) => row == null ? null : BuildLineRow(row), supportsRecycling: false);
        _lineList.SelectionChanged += (_, _) =>
        {
            if (_rebuilding) return;
            _pending = ShowSelectedLineAsync();
        };

        // A search result's manuscript, opened: its lines listed and this one
        // chosen. Return does the same - on the way down, because the list's
        // own keyboard handling would otherwise take it first.
        _lineList.DoubleTapped += async (_, _) => await OpenSelectedResultsManuscriptAsync();
        _lineList.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.Enter || !_searchResults) return;
            e.Handled = true;
            await OpenSelectedResultsManuscriptAsync();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _lineCount = new TextBlock { Classes = { "caption" } };

        // What stands where the lines would be when there are none to list:
        // a manuscript not downloaded yet, a search that found nothing.
        _linesEmptyArt = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };
        _linesEmptyMessage = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 };
        _linesEmpty = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false,
            Children = { _linesEmptyArt, _linesEmptyMessage }
        };
        var linesLayers = new Grid { Children = { _lineList, _linesEmpty } };
        var linesBody = WithColumnHeads(linesLayers, LineColumns, ("TRANSCRIPTION", false), ("ZONE", false));
        var linesPane = ToolPane.Build("LINES", linesBody, _lineCount);
        _linesTitle = FindSectionTitle(linesPane);

        // --- the photograph --------------------------------------------
        _specimen = new ManuscriptImageView { Zoom = ManuscriptImageZoom.FitHeight, FitPreset = ManuscriptImageZoom.FitHeight };
        _specimen.ZoomChanged += (_, _) => ShowZoom();

        _zoom = new ComboBox
        {
            Width = 136,
            FontSize = 12,
            MinHeight = 26,
            Padding = new Thickness(8, 2),
            PlaceholderText = "Zoomed",
            ItemsSource = new[] { "Fit the height", "Fit the width", "50%", "100%", "200%" },
            SelectedIndex = 0
        };
        _zoom.SelectionChanged += (_, _) =>
        {
            if (_settingZoom || _zoom.SelectedIndex < 0) return;
            _specimen.Zoom = _zoom.SelectedIndex switch
            {
                0 => ManuscriptImageZoom.FitHeight,
                1 => ManuscriptImageZoom.FitWidth,
                2 => ManuscriptImageZoom.Half,
                3 => ManuscriptImageZoom.Actual,
                _ => ManuscriptImageZoom.Double
            };
        };
        _zoomPercent = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Right };

        var zoomOut = ZoomButton("Minus", "Zoom out (⌘-)", () => _specimen.ZoomOut());
        var zoomIn = ZoomButton("Plus", "Zoom in (⌘+)", () => _specimen.ZoomIn());
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _zoomPercent, zoomOut, zoomIn, _zoom } };

        // The transcription sits under the photograph in a size meant to be
        // compared with it, not skimmed - this is the only place in the
        // application where the two are read against each other. Selectable,
        // and drawn by a text block that never treats a character as markup,
        // because these are somebody else's transcriptions and this is the
        // one place where what is on screen has to be character for character
        // what the file says.
        _transcription = new SelectableTextBlock
        {
            FontFamily = _medievalFont,
            FontSize = 19,
            LineHeight = 27,
            MinHeight = 27,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var photographPane = ToolPane.Build("PHOTOGRAPH", _specimen, zoomRow, _transcription);

        var right = new Grid { RowDefinitions = new RowDefinitions("*,12,270") };
        var rightSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(rightSplitter, 1);
        Grid.SetRow(photographPane, 2);
        right.Children.Add(linesPane);
        right.Children.Add(rightSplitter);
        right.Children.Add(photographPane);

        // Wide enough for the four columns; the reader can drag it either way.
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("470,12,*") };
        var bodySplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(bodySplitter, 1);
        Grid.SetColumn(right, 2);
        body.Children.Add(manuscriptsPane);
        body.Children.Add(bodySplitter);
        body.Children.Add(right);

        // --- what is selected, and what can be done with it --------------
        _description = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19, Text = Introduction() };

        _getTextButton = ToolPane.GlyphButton("Download Transcriptions", "Download");
        _getTextButton.Click += async (_, _) => await DownloadTextAsync();

        // The size is not on the button. It changes with the selection, and a
        // button whose caption grows by six characters either jumps about or
        // has to be sized for the widest manuscript in the catalogue - which
        // is "1.2 GB". The panel beside it gives the size, and the
        // confirmation states it again before anything starts.
        _getImagesButton = ToolPane.GlyphButton("Download Photographs", "Camera");
        _getImagesButton.Click += async (_, _) => await DownloadImagesAsync();

        // The page a line was cut out of, from the library that holds the
        // book - the decorated initials, the rubrics, the marginalia, the
        // illumination. Nothing is downloaded: see ManuscriptPagesWindow.
        _pagesButton = ToolPane.GlyphButton("See the Pages", "Manuscript");
        _pagesButton.Click += (_, _) => ShowPages();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 0, 0, 0) };
        buttons.Children.Add(_getTextButton);
        buttons.Children.Add(_getImagesButton);
        buttons.Children.Add(_pagesButton);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_description);

        _progress = new ProgressBar { IsIndeterminate = true, Width = 90, MinWidth = 90, Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsVisible = false };
        _stopButton = new Button { Classes = { "link" }, Content = "Stop", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), IsVisible = false };
        _stopButton.Click += (_, _) => _work?.Cancel();
        _statusLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        var statusRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_progress, Dock.Left);
        DockPanel.SetDock(_stopButton, Dock.Right);
        statusRow.Children.Add(_progress);
        statusRow.Children.Add(_stopButton);
        statusRow.Children.Add(_statusLabel);

        var layout = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(statusRow, Dock.Bottom);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(statusRow);
        layout.Children.Add(footer);
        layout.Children.Add(body);
        Content = layout;

        UpdateButtons();
        ShowZoom();

        KeyDown += (_, e) =>
        {
            // ⌘F puts the caret in the search box, as every Mac window with
            // one does.
            if (e.Key == Key.F && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
            else if (_specimen.TryHandleZoomKey(e))
            {
                e.Handled = true;
            }
        };

        Closed += (_, _) =>
        {
            _closed = true;
            _work?.Cancel();
            _imageLoading?.Cancel();
        };

        Opened += (_, _) =>
        {
            FitToScreen(this);
            _pending = ReloadAsync();
        };

        // Modeless, so Setup's CATMuS step can run while this is open; coming
        // back to it shows what that step added rather than a list that still
        // says nothing is here.
        Activated += (_, _) =>
        {
            if (_work == null && !_closed && _pending.IsCompleted) _pending = ReloadIfChangedAsync();
        };
    }

    /// <summary>
    /// Where the line photographs are looked for - the download folder the
    /// reader chose, unless a test says otherwise. Asked each time rather
    /// than kept, because Settings can move it while this window is open.
    /// </summary>
    internal Func<string> DataRoot { get; set; } = () => DataFolderSettings.Root;

    // For the tests, which drive the window as a reader would.
    internal ComboBox LanguageFilter => _languageFilter;
    internal ComboBox CenturyFilter => _centuryFilter;
    internal ComboBox ScriptFilter => _scriptFilter;
    internal CheckBox DownloadedOnly => _downloadedOnly;
    internal ListBox ManuscriptList => _manuscriptList;
    internal ListBox LineList => _lineList;
    internal ComboBox ZoomBox => _zoom;
    internal ManuscriptImageView Specimen => _specimen;
    internal IReadOnlyList<ManuscriptRow> ShownManuscripts => _shown;
    internal IReadOnlyList<CatmusLine> Lines => _lines;
    internal bool ShowingSearchResults => _searchResults;
    internal string StatusText => _statusLabel.Text ?? string.Empty;
    internal string DescriptionText => _description.Text ?? string.Empty;
    internal string TranscriptionText => _transcription.Text ?? string.Empty;
    internal string LinesTitle => _linesTitle.Text ?? string.Empty;
    internal string LineCountText => _lineCount.Text ?? string.Empty;
    internal bool NotInstalledNoticeShown => _notInstalledNotice.IsVisible;
    internal string? EmptyLinesMessage => _linesEmpty.IsVisible ? _linesEmptyMessage.Text : null;
    internal Button GetTextButton => _getTextButton;
    internal Button GetImagesButton => _getImagesButton;
    internal Button PagesButton => _pagesButton;

    /// <summary>Waits until every load started so far has answered.</summary>
    internal async Task WhenIdleAsync()
    {
        Task current;
        do
        {
            current = _pending;
            await current;
            await _imageTask;
        }
        while (!ReferenceEquals(current, _pending));
    }

    internal void SelectManuscript(string shelfmark)
    {
        var row = _shown.FirstOrDefault(r => string.Equals(r.Manuscript.Shelfmark, shelfmark, StringComparison.Ordinal));
        _manuscriptList.SelectedItem = row;
        if (row != null) _manuscriptList.ScrollIntoView(row);
    }

    internal Task SearchAsync(string term, bool exactly)
    {
        _searchBox.Text = term;
        _exactCharacters.IsChecked = exactly;
        return RunSearchAsync();
    }

    internal Task OpenResultsManuscriptAsync() => OpenSelectedResultsManuscriptAsync();

    /// <summary>
    /// The face the transcriptions are set in. A diplomatic transcription is
    /// full of what ordinary fonts do not draw - ꝯ ꝑ ꝓ ꝰ ⁊ ſ, and combining
    /// marks stacked over letters - so the faces made for it come first:
    /// Junicode, then the MUFI fonts Andron Scriptor and Palemonas (under the
    /// names they install with as well as their short ones), then Noto Serif,
    /// and the reading face last - every name in its list, not only the
    /// first. Whatever is installed draws what it can.
    /// </summary>
    internal static FontFamily TranscriptionFont() =>
        new("Junicode, Andron Scriptor Web, Andron Scriptor, Palemonas MUFI, Palemonas, Noto Serif, " +
            string.Join(", ", ToolPane.Font("Cc.SourceFont").FamilyNames));

    private static string Introduction() =>
        $"{CatmusCatalogue.Manuscripts.Count} manuscripts, {CatmusCatalogue.TotalLines:N0} lines, " +
        "7th to 16th century, from CATMuS-Medieval. Transcriptions are a small download; " +
        "the photographs are the rest of the 24.7 GB, so they come one manuscript at a time.";

    private async Task ReloadAsync(string? select = null, bool keepStatus = false)
    {
        List<CatmusHolding> holdings;
        try
        {
            holdings = await Task.Run(() => _repository.GetHoldingsAsync());
        }
        catch (Exception ex)
        {
            if (_closed) return;
            SetStatus("Could not read the library: " + ex.Message);
            holdings = new List<CatmusHolding>();
        }

        if (_closed) return;

        _held = holdings.ToDictionary(h => h.Shelfmark, StringComparer.Ordinal);
        _notInstalledNotice.IsVisible = _held.Count == 0;
        ShowManuscripts(select, keepStatus);
    }

    /// <summary>
    /// Reloads only if the library's holdings have changed since they were
    /// last read - so coming back to the window does not throw away a search
    /// or the line being looked at for nothing.
    /// </summary>
    private async Task ReloadIfChangedAsync()
    {
        List<CatmusHolding> holdings;
        try
        {
            holdings = await Task.Run(() => _repository.GetHoldingsAsync());
        }
        catch (Exception)
        {
            return;
        }

        if (_closed || _work != null) return;

        static string Signature(IEnumerable<CatmusHolding> held) =>
            string.Join("\n", held.OrderBy(h => h.Shelfmark, StringComparer.Ordinal).Select(h => $"{h.Shelfmark}\t{h.LineCount}\t{h.ImagePack}"));

        if (Signature(holdings) == Signature(_held.Values)) return;
        await ReloadAsync();
    }

    private void ShowManuscripts() => ShowManuscripts(null, keepStatus: false);

    private void ShowManuscripts(string? select, bool keepStatus)
    {
        // Kept across the rebuild. A download finishes by reloading the
        // library, and without this the list would jump back to its first
        // manuscript - so fetching a gigabyte of photographs for the two
        // hundredth one would end by showing the first one instead.
        var wasSelected = select ?? SelectedManuscript?.Shelfmark;

        var language = _languageFilter.SelectedIndex > 0 ? (string)_languageFilter.SelectedItem! : null;
        var script = _scriptFilter.SelectedIndex > 0 ? (string)_scriptFilter.SelectedItem! : null;
        var century = _centuryFilter.SelectedIndex > 0
            ? CatmusCatalogue.Centuries[_centuryFilter.SelectedIndex - 1]
            : (int?)null;

        _shown = CatmusCatalogue.Manuscripts
            .Where(m => language == null || m.Language.Contains(language, StringComparison.Ordinal))
            .Where(m => script == null || m.ScriptType.Contains(script, StringComparison.Ordinal))
            .Where(m => century == null || m.Century == century)
            .Where(m => _downloadedOnly.IsChecked != true || _held.ContainsKey(m.Shelfmark))
            .Select(m => new ManuscriptRow(m, _held.GetValueOrDefault(m.Shelfmark)))
            .ToList();

        _rebuilding = true;
        _manuscriptList.ItemsSource = _shown;
        _rebuilding = false;

        _manuscriptCount.Text = $"{_shown.Count} of {CatmusCatalogue.Manuscripts.Count}";

        // A download's outcome is not overwritten by the count. On Windows
        // the reload that follows a download replaced "Could not finish:" with
        // the manuscript count before anyone could read it.
        if (!keepStatus) SetStatus($"{_shown.Count} of {CatmusCatalogue.Manuscripts.Count} manuscripts");

        if (_shown.Count == 0)
        {
            ++_linesGeneration;
            _searchResults = false;
            _lines = new List<CatmusLine>();
            _description.Text = Introduction();
            ShowLines("No manuscript in the catalogue matches these filters.");
            UpdateButtons();
            return;
        }

        var index = wasSelected == null
            ? 0
            : Math.Max(0, _shown.FindIndex(r => string.Equals(r.Manuscript.Shelfmark, wasSelected, StringComparison.Ordinal)));

        _manuscriptList.SelectedIndex = index;
        _manuscriptList.ScrollIntoView(index);
    }

    /// <summary>The manuscript selected in the list - null while there is none.</summary>
    private CatmusManuscript? SelectedManuscript => (_manuscriptList.SelectedItem as ManuscriptRow)?.Manuscript;

    /// <summary>
    /// The manuscript the panel describes and the buttons act on: the one
    /// selected in the list, except while a search result is selected, when
    /// it is that line's own manuscript.
    ///
    /// Windows always describes the manuscript selected in the list, so a
    /// line found in a Castilian manuscript under CC-BY-NC-SA was shown beside
    /// the CC-BY terms of whichever manuscript happened to be selected. The
    /// terms beside a line have to be that line's.
    /// </summary>
    private CatmusManuscript? ManuscriptInView =>
        _searchResults && _lineList.SelectedItem is LineRow row
            ? CatmusCatalogue.Find(row.Line.Shelfmark)
            : SelectedManuscript;

    private async Task ShowSelectedManuscriptAsync()
    {
        var manuscript = SelectedManuscript;
        if (manuscript == null)
        {
            UpdateButtons();
            return;
        }

        var generation = ++_linesGeneration;
        _searchResults = false;
        UpdateButtons();
        _description.Text = DescribeManuscript(manuscript);

        if (!_held.TryGetValue(manuscript.Shelfmark, out var holding))
        {
            _lines = new List<CatmusLine>();
            ShowLines("Not downloaded yet.");
            return;
        }

        List<CatmusLine> lines;
        try
        {
            lines = await Task.Run(() => _repository.GetLinesAsync(holding.ManuscriptId, limit: LineLimit));
        }
        catch (Exception ex)
        {
            if (!_closed && generation == _linesGeneration) SetStatus("Could not read the lines: " + ex.Message);
            return;
        }

        if (_closed || generation != _linesGeneration) return;

        _lines = lines;
        ShowLines(null, holding.LineCount);
    }

    private string DescribeManuscript(CatmusManuscript manuscript)
    {
        var held = _held.TryGetValue(manuscript.Shelfmark, out var holding);

        var facts = $"{manuscript.Shelfmark} - {manuscript.Language}, " +
                    $"{manuscript.Century}{OrdinalSuffix(manuscript.Century ?? 0)} century, " +
                    $"{manuscript.ScriptType}, {manuscript.Genre}, {manuscript.Verse}. " +
                    $"{manuscript.Lines:N0} lines; photographs {FormatBytes(manuscript.Bytes)}.";

        var state = !held
            ? "Not downloaded."
            : holding!.HasImages
                ? "Transcriptions and photographs are on this computer."
                : "Transcriptions are on this computer; photographs are not.";

        // Said here rather than left to a greyed-out button, because only
        // about a third of these manuscripts can be shown as pages and a
        // reader clicking through the list deserves to know which without
        // having to notice a disabled control.
        var pages = CatmusManifests.Has(manuscript.Shelfmark)
            ? "Its pages can be seen."
            : "Its pages are not published anywhere this can reach.";

        // The photographs' own terms are deliberately not here. They run to
        // three lines, they are the same sentence for every manuscript, and
        // this panel has room for two - so they are said where they are
        // actually needed, in the confirmation before a download, in the
        // window that shows the pages, and again in Help.
        //
        // The credit the transcriptions' licence asks for stands beside the
        // licence. On Windows it was in the introduction only, which a
        // selection replaces the moment the window opens.
        return $"{facts}\n{state}  {pages}  From {CatmusLicence.Attribution}. {LicenceOf(manuscript)}";
    }

    /// <summary>
    /// The transcriptions' terms, decided shard by shard.
    ///
    /// The licence follows the transcription project, and a manuscript can be
    /// more than one project: Paris, BnF, esp. 36 is part HTRomance and part
    /// Towards General Castilian HTR. Its joined project name matches neither
    /// exactly, so asking CatmusLicence about the manuscript as a whole would
    /// call all of it CC-BY - including the half that is share-alike and
    /// non-commercial. Asked of each shard, the answer is right for both.
    /// </summary>
    internal static string LicenceOf(CatmusManuscript manuscript)
    {
        var nonCommercial = manuscript.Shards.Count(s => CatmusLicence.IsNonCommercial(s.Project));

        if (nonCommercial == 0) return CatmusLicence.Describe(manuscript.Shards.FirstOrDefault()?.Project);
        if (nonCommercial == manuscript.Shards.Count) return CatmusLicence.Describe(CatmusLicence.NonCommercialProject);

        return $"Transcriptions: mixed - the lines from {CatmusLicence.NonCommercialProject} are CC-BY-NC-SA 4.0, " +
               "share alike, non-commercial use only; the rest are CC-BY 4.0, free to reuse with attribution.";
    }

    /// <summary>Fills the line list, choosing the first line - or the one a search result asked for.</summary>
    private void ShowLines(string? emptyMessage, int? totalLines = null)
    {
        _linesTitle.Text = _searchResults ? "LINES FOUND ACROSS THE LIBRARY" : "LINES";
        _lineCount.Text = _lines.Count == 0
            ? string.Empty
            : totalLines is { } total && total > _lines.Count
                ? $"first {_lines.Count:N0} of {total:N0}"
                : $"{_lines.Count:N0}";

        var rows = _lines.Select(l => new LineRow(l, _searchResults)).ToList();

        _rebuilding = true;
        _lineList.ItemsSource = rows;
        _rebuilding = false;

        _linesEmpty.IsVisible = rows.Count == 0;
        _linesEmptyMessage.Text = emptyMessage ?? "No lines.";
        _linesEmptyArt.Content = rows.Count == 0 ? EmptyArt(emptyMessage) : null;

        if (rows.Count > 0)
        {
            var index = 0;
            if (_pendingLineId is { } wanted)
            {
                index = Math.Max(0, rows.FindIndex(r => r.Line.LineId == wanted));
                _pendingLineId = null;
            }

            _lineList.SelectedIndex = index;
            _lineList.ScrollIntoView(index);
            return;
        }

        _pendingLineId = null;
        _imageLoading?.Cancel();
        _transcription.Text = string.Empty;

        // With no line chosen, the panel goes back to the manuscript chosen
        // in the list - which is also what the buttons now act on.
        if (_searchResults)
        {
            _description.Text = SelectedManuscript is { } selected ? DescribeManuscript(selected) : Introduction();
        }

        // Said over the empty list, where the lines would be, rather than in
        // the photograph's strip, which is sized for a line and not for an
        // explanation.
        _specimen.Show(null, null);
        UpdateButtons();
    }

    private static Control? EmptyArt(string? message)
    {
        switch (message)
        {
            case "Not downloaded yet.":
                return Art.Tile("medieval-hands", "MedievalHands", 64);
            case "Nothing found.":
            {
                var picture = Art.Load("empty-results.png");
                return picture == null
                    ? Art.Tile("medieval-hands", "MedievalHands", 64)
                    : new Image { Source = picture, Width = 160, Height = 120, Stretch = Stretch.Uniform };
            }
            default:
                return null;
        }
    }

    private Task ShowSelectedLineAsync()
    {
        if (_lineList.SelectedItem is not LineRow row) return Task.CompletedTask;

        var line = row.Line;
        _transcription.Text = line.Text;

        if (_searchResults)
        {
            var manuscript = ManuscriptInView;
            // A shelfmark the embedded catalogue does not know - the ingest
            // takes it from the dataset's own rows, which can move on - still
            // gets its terms, from the project the library recorded for it.
            _description.Text = manuscript != null
                ? DescribeManuscript(manuscript)
                : $"{line.Shelfmark} - a manuscript the catalogue in this version does not list.  " +
                  $"From {CatmusLicence.Attribution}. {CatmusLicence.Describe(_held.GetValueOrDefault(line.Shelfmark)?.Project)}";
        }

        UpdateButtons();

        _imageLoading?.Cancel();
        _imageLoading = new CancellationTokenSource();
        var token = _imageLoading.Token;

        if (!line.HasImage)
        {
            _specimen.Show(null,
                "The photograph of this line has not been downloaded. " +
                "Use Download Photographs to fetch this manuscript's.");
            return Task.CompletedTask;
        }

        _imageTask = LoadLineImageAsync(line, token);
        return _imageTask;
    }

    private async Task LoadLineImageAsync(CatmusLine line, CancellationToken token)
    {
        var root = DataRoot();

        (Bitmap? Image, string? Failure) result;
        try
        {
            result = await Task.Run(async () =>
            {
                var bytes = await CatmusImageDownloadService.ReadImageAsync(root, line, token);
                if (bytes == null) return ((Bitmap?)null, "The photograph could not be read from the download folder.");

                try
                {
                    // Decoded here, off the UI thread, all at once - Avalonia
                    // reads the whole stream, so nothing has to outlive it.
                    using var stream = new MemoryStream(bytes);
                    return (new Bitmap(stream), (string?)null);
                }
                catch (Exception)
                {
                    return (null, "The photograph is not an image this computer can read.");
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Another line was chosen while this one was read. Whichever read is
        // still running owns the panel now.
        if (token.IsCancellationRequested || _closed)
        {
            result.Image?.Dispose();
            return;
        }

        _specimen.Show(result.Image, result.Failure);
    }

    private void ShowZoom()
    {
        // The picker follows the view, so a pinch shows "Zoomed" and ⌘9 or a
        // double-click puts the preset it went to back in the picker.
        _settingZoom = true;
        _zoom.SelectedIndex = _specimen.IsCustomZoom ? -1 : _specimen.Zoom switch
        {
            ManuscriptImageZoom.FitHeight => 0,
            ManuscriptImageZoom.FitWidth => 1,
            ManuscriptImageZoom.Half => 2,
            ManuscriptImageZoom.Actual => 3,
            ManuscriptImageZoom.Double => 4,
            _ => -1
        };
        _settingZoom = false;

        _zoomPercent.Text = _specimen.Bitmap == null ? string.Empty : $"{_specimen.Scale * 100:0}%";
    }

    private void UpdateButtons()
    {
        var manuscript = ManuscriptInView;
        var busy = _work != null;
        var held = manuscript != null && _held.ContainsKey(manuscript.Shelfmark);

        _getTextButton.IsEnabled = manuscript != null && !busy && !held;
        _getImagesButton.IsEnabled = manuscript != null && !busy && held && !_held[manuscript!.Shelfmark].HasImages;

        // Independent of whether anything has been downloaded: the pages come
        // from the library that holds the book, not from this library. It
        // needs nothing but a connection.
        _pagesButton.IsEnabled = manuscript != null && CatmusManifests.Has(manuscript.Shelfmark);

        _progress.IsVisible = busy;
        _stopButton.IsVisible = busy;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>
    /// The manuscript's pages, from the library that holds it, in a window of
    /// their own beside this one - one per manuscript, brought forward if it
    /// is already open, so lines and leaves can be looked at side by side.
    /// </summary>
    private void ShowPages()
    {
        var manuscript = ManuscriptInView;
        if (manuscript == null) return;

        var manifest = CatmusManifests.For(manuscript.Shelfmark);
        if (manifest == null) return;

        if (_pagesWindows.TryGetValue(manuscript.Shelfmark, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        var pages = new ManuscriptPagesWindow(manuscript.Shelfmark, manifest);
        pages.Closed += (_, _) => _pagesWindows.Remove(manuscript.Shelfmark);
        _pagesWindows[manuscript.Shelfmark] = pages;
        pages.Show(this);
    }

    private async Task DownloadTextAsync()
    {
        var manuscript = ManuscriptInView;
        if (manuscript == null) return;

        await RunAsync($"Reading {manuscript.Shelfmark}", manuscript.Shelfmark, async (progress, token) =>
        {
            var service = new CatmusIngestService(_repository);
            await service.IngestAsync(manuscript.Shards, progress, token);
        });
    }

    private async Task DownloadImagesAsync()
    {
        var manuscript = ManuscriptInView;
        if (manuscript == null) return;

        // Asked before it starts rather than reported while it runs: a
        // gigabyte is a thing to agree to, and Ghent UL 1374 really is one.
        var agreed = await Alert.Confirm(this,
            $"Download the line photographs of {manuscript.Shelfmark}?",
            $"{FormatBytes(manuscript.Bytes)} over {manuscript.Shards.Count} file(s), " +
            $"for {manuscript.Lines:N0} lines.\n\n{CatmusLicence.ImageTerms}",
            "Download");

        if (!agreed) return;

        var root = DataRoot();
        await RunAsync($"Fetching photographs of {manuscript.Shelfmark}", manuscript.Shelfmark, async (progress, token) =>
        {
            var service = new CatmusImageDownloadService(_repository);
            await service.DownloadAsync(manuscript, root, progress, token);
        });
    }

    /// <summary>
    /// Runs one download with the buttons disabled, the status line live, and
    /// the library reloaded afterwards whatever happened - including a
    /// cancellation, which may still have finished some of the shards. Stop
    /// cancels it, and so does closing the window.
    /// </summary>
    private async Task RunAsync(string what, string shelfmark, Func<IProgress<string>, CancellationToken, Task> work)
    {
        if (_work != null) return;

        _work = new CancellationTokenSource();
        var token = _work.Token;
        UpdateButtons();
        SetStatus(what + "...");

        // Guarded because the window can be closed mid-download: closing
        // cancels the token, but the reports already posted still arrive -
        // the same shape of bug as a cancelled setup step reporting into a
        // closed wizard.
        var progress = new Progress<string>(message =>
        {
            if (!_closed) SetStatus(message);
        });

        try
        {
            // Off the UI thread: the network is asynchronous, but the
            // Parquet decoding and the SQLite writes are not.
            await Task.Run(() => work(progress, token), token);
            if (!_closed) SetStatus(what + " - done.");
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SetStatus(what + " - stopped.");
        }
        catch (Exception ex)
        {
            if (!_closed) SetStatus("Could not finish: " + ex.Message);
        }
        finally
        {
            _work.Dispose();
            _work = null;

            if (!_closed)
            {
                // Reloaded even after a cancellation: a stopped run may still
                // have finished some of a manuscript's files, and those are
                // in the library whether or not the rest arrived.
                UpdateButtons();
                await ReloadAsync(select: shelfmark, keepStatus: true);
            }
        }
    }

    internal async Task RunSearchAsync()
    {
        var term = (_searchBox.Text ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            await ShowSelectedManuscriptAsync();
            return;
        }

        var language = _languageFilter.SelectedIndex > 0 ? (string)_languageFilter.SelectedItem! : null;
        var script = _scriptFilter.SelectedIndex > 0 ? (string)_scriptFilter.SelectedItem! : null;
        var century = _centuryFilter.SelectedIndex > 0
            ? CatmusCatalogue.Centuries[_centuryFilter.SelectedIndex - 1]
            : (int?)null;
        var exactly = _exactCharacters.IsChecked == true;

        var generation = ++_linesGeneration;
        _searchButton.IsEnabled = false;
        SetStatus("Searching...");

        List<CatmusLine> lines;
        try
        {
            lines = await Task.Run(() => _repository.SearchAsync(
                term, exactly, manuscriptId: null,
                language: language, scriptType: script, century: century, limit: SearchLimit));
        }
        catch (Exception ex)
        {
            if (!_closed && generation == _linesGeneration) SetStatus("Search failed: " + ex.Message);
            return;
        }
        finally
        {
            _searchButton.IsEnabled = true;
        }

        if (_closed || generation != _linesGeneration) return;

        _lines = lines;
        _searchResults = true;

        if (_lines.Count == 0 && !exactly)
        {
            // A search for a sign that is not a letter - the Tironian et, a
            // bare combining mark - folds away to nothing, so the folded
            // search can only ever return nothing and saying "no results"
            // would be misleading about why.
            var folded = term.Any(char.IsLetter);
            SetStatus(folded
                ? "Nothing found."
                : "That sign has no letters in it - tick \"Exactly as written\" to search for the sign itself.");
        }
        else
        {
            SetStatus($"{_lines.Count} line(s) across the library" +
                      (_lines.Count == SearchLimit ? $" (first {SearchLimit})" : "") +
                      (_lines.Count > 0 ? ". Double-click one to open its manuscript." : "."));
        }

        ShowLines("Nothing found.");
    }

    /// <summary>
    /// Lists a search result's manuscript with that line chosen in it, so its
    /// neighbours in the dataset can be looked at and its manuscript's
    /// buttons used.
    /// </summary>
    private async Task OpenSelectedResultsManuscriptAsync()
    {
        if (!_searchResults || _lineList.SelectedItem is not LineRow row) return;

        var target = _shown.FirstOrDefault(r => string.Equals(r.Manuscript.Shelfmark, row.Line.Shelfmark, StringComparison.Ordinal));
        if (target == null)
        {
            SetStatus($"{row.Line.Shelfmark} is not in the list as it is filtered.");
            return;
        }

        _pendingLineId = row.Line.LineId;

        if (ReferenceEquals(_manuscriptList.SelectedItem, target))
        {
            await ShowSelectedManuscriptAsync();
        }
        else
        {
            _manuscriptList.SelectedItem = target;
            _manuscriptList.ScrollIntoView(target);
            await _pending;
        }
    }

    /// <summary>
    /// Keeps a window inside the display it opens on. These windows are sized
    /// for the lists and the photograph side by side, and on a 13-inch
    /// MacBook, with the menu bar and the Dock taken off, that is taller than
    /// the room there is - which would open with the buttons along the bottom
    /// out of reach. Everything inside is laid out to stretch, so shrinking
    /// costs nothing but smaller panes.
    /// </summary>
    internal static void FitToScreen(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;

        var area = screen.WorkingArea;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var roomWidth = area.Width / scaling;
        var roomHeight = area.Height / scaling;
        if (window.Width <= roomWidth && window.Height <= roomHeight) return;

        window.Width = Math.Min(window.Width, roomWidth);
        window.Height = Math.Min(window.Height, roomHeight);
        window.Position = new PixelPoint(
            area.X + Math.Max(0, (int)((area.Width - window.Width * scaling) / 2)),
            area.Y + Math.Max(0, (int)((area.Height - window.Height * scaling) / 2)));
    }

    internal static string OrdinalSuffix(int value) => value switch
    {
        11 or 12 or 13 => "th",
        _ when value % 10 == 1 => "st",
        _ when value % 10 == 2 => "nd",
        _ when value % 10 == 3 => "rd",
        _ => "th"
    };

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1073741824 => $"{bytes / 1073741824.0:F1} GB",
        >= 1048576 => $"{bytes / 1048576.0:F0} MB",
        _ => $"{bytes / 1024.0:F0} KB"
    };

    // --- drawing -----------------------------------------------------------

    private const string ManuscriptColumns = "*,40,104,60";
    private const string LineColumns = "*,112";

    private Control BuildManuscriptRow(ManuscriptRow row)
    {
        var m = row.Manuscript;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ManuscriptColumns), Margin = new Thickness(10, 5, 10, 5) };

        var shelfmark = Cell(m.Shelfmark, 0);
        ToolTip.SetTip(shelfmark, m.Shelfmark);
        ToolTip.SetShowDelay(shelfmark, 600);

        grid.Children.Add(shelfmark);
        grid.Children.Add(Cell(m.Century?.ToString() ?? string.Empty, 1, right: true));
        grid.Children.Add(Cell(m.ScriptType, 2, leftGap: 12));
        grid.Children.Add(Cell(row.Photos, 3, right: true));

        // A manuscript whose transcriptions are not downloaded is shown in
        // the muted colour rather than hidden: the catalogue is the point of
        // this list, and greying is the difference between "you do not have
        // this yet" and "this does not exist".
        if (!row.Held)
        {
            foreach (var cell in grid.Children.OfType<TextBlock>()) cell.Classes.Add("muted");
        }

        return grid;
    }

    private Control BuildLineRow(LineRow row)
    {
        var line = row.Line;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(LineColumns), Margin = new Thickness(10, 4, 10, 4) };

        var text = new TextBlock
        {
            FontFamily = _medievalFont,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        // A search result says which manuscript it is from - which is why
        // the line carries its shelfmark - in the quiet lead every result
        // list here uses.
        if (row.FromSearch)
        {
            var lead = new Run(line.Shelfmark + ":  ") { FontFamily = FontFamily.Default, FontSize = 12, FontWeight = FontWeight.SemiBold };
            lead.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
            text.Inlines!.Add(lead);
            text.Inlines.Add(new Run(line.Text));
        }
        else
        {
            text.Text = line.Text;
        }

        ToolTip.SetTip(text, line.Text);
        ToolTip.SetShowDelay(text, 800);

        var zone = Cell(line.Region ?? string.Empty, 1, leftGap: 12);
        zone.Classes.Add("caption");

        // A line whose photograph is not here is muted, as on Windows.
        if (!line.HasImage) text.Classes.Add("muted");

        grid.Children.Add(text);
        grid.Children.Add(zone);
        return grid;
    }

    private static TextBlock Cell(string text, int column, bool right = false, double leftGap = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            Margin = new Thickness(leftGap, 0, 0, 0)
        };
        Grid.SetColumn(block, column);
        return block;
    }

    /// <summary>A list with a row of column headings over it, the headings laid out on the same columns as the rows.</summary>
    private static Control WithColumnHeads(Control list, string columns, params (string Title, bool Right)[] heads)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), Margin = new Thickness(14, 7, 18, 6) };
        for (var i = 0; i < heads.Length; i++)
        {
            var head = new TextBlock
            {
                Text = heads[i].Title,
                Classes = { "section" },
                FontSize = 10,
                TextAlignment = heads[i].Right ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(i > 0 && !heads[i].Right ? 12 : 0, 0, 0, 0)
            };
            Grid.SetColumn(head, i);
            grid.Children.Add(head);
        }

        var headBorder = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        headBorder.Bind(Border.BorderBrushProperty, headBorder.GetResourceObservable("Cc.Rule"));

        var dock = new DockPanel();
        DockPanel.SetDock(headBorder, Dock.Top);
        dock.Children.Add(headBorder);
        dock.Children.Add(list);
        return dock;
    }

    private static ListBox CompactList()
    {
        var list = new ListBox { SelectionMode = SelectionMode.Single, Padding = new Thickness(4, 4) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);

        // Fluent pads a list item for touch; a table reads better set close,
        // with the row supplying its own inset.
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.MinHeightProperty, 0d)
            }
        });
        return list;
    }

    /// <summary>The section title ToolPane draws in a pane's header, so it can say which lines are listed.</summary>
    private static TextBlock FindSectionTitle(Control pane) =>
        pane.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("section"));

    private static ComboBox Combo(double width, string any, IEnumerable<string> values) =>
        new() { Width = width, ItemsSource = new[] { any }.Concat(values).ToList(), SelectedIndex = 0, MaxDropDownHeight = 420 };

    private static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 0) };
        panel.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        control.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(control);
        return panel;
    }

    private static Control Spaced(Control control)
    {
        control.Margin = new Thickness(0, 0, 20, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static Button ZoomButton(string glyph, string tip, Action action)
    {
        var button = new Button { Classes = { "toolbar" }, Content = Glyphs.Icon(glyph, 13), Height = 26, MinWidth = 26, Padding = new Thickness(5, 3) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}

/// <summary>One manuscript of the catalogue, and what this library holds of it.</summary>
internal sealed class ManuscriptRow(CatmusManuscript manuscript, CatmusHolding? holding)
{
    public CatmusManuscript Manuscript { get; } = manuscript;

    public CatmusHolding? Holding { get; } = holding;

    public bool Held => Holding != null;

    public string Photos => Holding?.HasImages == true ? "held" : MedievalHandsWindow.FormatBytes(Manuscript.Bytes);
}

/// <summary>A line as the list draws it: from one manuscript, or found across the library.</summary>
internal sealed class LineRow(CatmusLine line, bool fromSearch)
{
    public CatmusLine Line { get; } = line;

    public bool FromSearch { get; } = fromSearch;
}
