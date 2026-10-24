using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Artifacts;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.PlacesMap;

/// <summary>One passage that mentions the place clicked, with any tags the reader has put on it.</summary>
internal sealed record PlacePassage(
    int WorkId,
    long TextNodeId,
    string AuthorName,
    string WorkTitle,
    string CitationRef,
    string Text,
    string? Milestone,
    IReadOnlyList<string> Tags);

/// <summary>
/// The Places Map: the ancient world with every place in the reference list
/// pinned on it, each kind of place in its own colour. Click a pin and the
/// library is searched for the place's name; the passages that mention it are
/// listed beside the map, double-click one to read it, and any objects from
/// the Art &amp; Archaeology catalogue found there are shown above them.
///
/// Every place in the reference list is on one footing. The map once pinned
/// tagged places in one colour and the rest in another, and that split was
/// worth nothing here: the reference list is loaded for everyone, so on a
/// fresh library every pin was the same colour anyway, and a place you happened
/// to have tagged behaved differently from an identical one beside it for
/// reasons the map couldn't show. Tags still surface, but where they mean
/// something - against the passages themselves, in the list.
/// </summary>
public sealed class PlacesMapWindow : FeatureWindow
{
    private readonly MapCanvas _canvas = new();

    /// <summary>The legend's swatches, recoloured when the appearance changes.</summary>
    private readonly List<(PlaceKind Kind, Ellipse Swatch)> _kindSwatches = new();

    private readonly ArtifactBrowser _artifactBrowser;
    private readonly ListBox _passageList;
    private readonly TextBlock _placeTitle;
    private readonly TextBlock _placeDetail;
    private readonly TextBlock _resultsLabel;
    private readonly TextBlock _resultsStatus;
    private readonly Button _zoomIn;
    private readonly Button _zoomOut;
    private readonly Button _zoomHome;

    private readonly TagRepository _tagRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly ArtifactRepository _artifactRepo = new();

    private List<PlacePassage> _currentPassages = new();
    private bool _truncated;

    /// <summary>
    /// The place whose passages are listed, kept apart from the sentence above
    /// the list - "Search results for “Delphi” (double-click to jump):" is right
    /// for the screen and wrong for anything that needs the bare name, like the
    /// title of an export.
    /// </summary>
    private string _selectedPlaceName = string.Empty;

    /// <summary>
    /// Counts clicks, so a slow search for the place clicked first cannot
    /// overwrite the answer for the place clicked since.
    /// </summary>
    private int _request;
    private bool _closed;
    private bool? _hasCatalogue;

    public PlacesMapWindow()
    {
        Title = "Places Map";
        Width = 1260;
        Height = 820;
        MinWidth = 920;
        MinHeight = 600;

        var intro = new TextBlock
        {
            Text = "Click any place to search the library for it. Passages you have tagged are marked in the results. " +
                   "Pinch to zoom (or scroll, with a mouse wheel), drag or swipe with two fingers to pan, and " +
                   "double-click open sea to reset the view. Untick a kind below to thin the map out.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 920,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // One checkbox per kind, with a swatch in its pin colour so the legend
        // is also the key. Between the instructions and the map rather than
        // beside it: the map is the widest thing in the window, and taking
        // width from it to save one row of height is a poor trade.
        var kinds = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var showIcon = new PathIcon
        {
            Data = Glyphs.Get("Show"),
            Width = 15,
            Height = 15,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        ToolTip.SetTip(showIcon, "Kinds of place shown on the map");
        kinds.Children.Add(showIcon);

        foreach (var kind in Enum.GetValues<PlaceKind>())
        {
            // The count is here because "Sanctuaries" covers a handful of places
            // and "Cities" well over a hundred, and knowing that before clicking
            // saves finding out by clicking.
            var count = PlaceData.All().Count(p => p.Kind == kind);

            // Colour on a swatch, where it only has to be itself; the label in
            // whatever colour text is meant to be. The pin colours are chosen to
            // read against the map's land and sea, not against a window, so the
            // words are not drawn in them.
            var swatch = new Ellipse { Width = 11, Height = 11, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };

            var box = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(0, 0, 18, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 7,
                    Children =
                    {
                        swatch,
                        new TextBlock { Text = $"{KindLabel(kind)} ({count})", VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };

            // The swatch keeps its colour whether the kind is shown or not. A
            // dimmed swatch reads as a second, weaker checkbox: the tick already
            // says whether the kind is on, and a key that changes colour is no
            // longer a key.
            var captured = kind;
            box.IsCheckedChanged += (_, _) => _canvas.SetKindVisible(captured, box.IsChecked == true);

            _kindSwatches.Add((kind, swatch));
            kinds.Children.Add(box);
        }

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 12) };
        header.Children.Add(intro);
        header.Children.Add(kinds);

        _canvas.PlaceClicked += marker => PendingLoad = ShowPlaceAsync(marker);
        _canvas.ViewChanged += UpdateZoomButtons;
        _canvas.AppearanceChanged += RecolourSwatches;

        _zoomIn = ZoomButton("Plus", "Zoom in (⌘+)", () => _canvas.ZoomBy(1.6));
        _zoomOut = ZoomButton("Minus", "Zoom out (⌘−)", () => _canvas.ZoomBy(1 / 1.6));
        _zoomHome = ZoomButton("Home", "Show the whole map (⌘0)", () => _canvas.ResetView());

        var zoomStack = new StackPanel();
        zoomStack.Children.Add(_zoomIn);
        zoomStack.Children.Add(new Border { Classes = { "rule" }, Margin = new Thickness(6, 0) });
        zoomStack.Children.Add(_zoomOut);
        zoomStack.Children.Add(new Border { Classes = { "rule" }, Margin = new Thickness(6, 0) });
        zoomStack.Children.Add(_zoomHome);

        var zoomPanel = new Border
        {
            Child = zoomStack,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 14)
        };
        zoomPanel.Bind(Border.BackgroundProperty, zoomPanel.GetResourceObservable("Cc.Card"));
        zoomPanel.Bind(Border.BorderBrushProperty, zoomPanel.GetResourceObservable("Cc.Rule"));
        zoomPanel.Bind(Border.BoxShadowProperty, zoomPanel.GetResourceObservable("Cc.CardShadow"));

        var mapLayers = new Grid();
        mapLayers.Children.Add(_canvas);
        mapLayers.Children.Add(zoomPanel);

        // The built-in shapes are a sketch, and say so where they are drawn -
        // the same caveat AncientWorldCoastline makes of itself: recognisable at
        // a glance, not something to measure a distance against.
        if (NaturalEarthCoastline.Load() == null)
        {
            var coastNote = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5),
                Margin = new Thickness(14, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = 0.92,
                Child = new TextBlock
                {
                    Text = "Coastlines are a rough hand-drawn sketch - add World Map Data in Setup for the real ones.",
                    Classes = { "caption" },
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                }
            };
            coastNote.Bind(Border.BackgroundProperty, coastNote.GetResourceObservable("Cc.Card"));
            mapLayers.Children.Add(coastNote);
        }

        var mapSurface = new Border { Classes = { "page" }, Child = mapLayers };

        // The column beside the map: the place, what was found there, and the
        // passages that name it.
        _placeTitle = new TextBlock { Classes = { "h2" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _placeDetail = new TextBlock
        {
            Text = "Click a place to see its passages here.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };

        _artifactBrowser = new ArtifactBrowser { PhotoHeight = 170, CaptionLines = 3 };

        _resultsLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), IsVisible = false };
        _resultsStatus = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        _passageList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<PlacePassage>((passage, _) => BuildPassageRow(passage), supportsRecycling: false)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_passageList, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _passageList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)) }
        });
        _passageList.DoubleTapped += async (_, _) => await JumpToSelectedPassageAsync();
        // Return opens the selected passage, as it opens a file in Finder. Seen
        // even when the list has handled the key itself.
        _passageList.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await JumpToSelectedPassageAsync();
        }, handledEventsToo: true);
        AttachPassageMenu();

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(18, 16, 18, 14)
        };
        Row(column, _placeTitle, 0);
        Row(column, _placeDetail, 1);
        Row(column, new Border { Classes = { "rule" }, Margin = new Thickness(0, 14, 0, 12) }, 2);
        Row(column, new TextBlock { Text = "OBJECTS FOUND HERE", Classes = { "section" }, Margin = new Thickness(0, 0, 0, 8) }, 3);
        Row(column, _artifactBrowser, 4);
        Row(column, new Border { Classes = { "rule" }, Margin = new Thickness(0, 14, 0, 12) }, 5);
        Row(column, new TextBlock { Text = "PASSAGES", Classes = { "section" }, Margin = new Thickness(0, 0, 0, 6) }, 6);
        Row(column, _resultsLabel, 7);
        Row(column, _passageList, 8);
        Row(column, _resultsStatus, 9);

        var side = new Border { Classes = { "page" }, Child = column, Width = 350 };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,14,Auto"),
            Margin = new Thickness(18, 0, 18, 18)
        };
        Grid.SetColumn(mapSurface, 0);
        Grid.SetColumn(side, 2);
        body.Children.Add(mapSurface);
        body.Children.Add(side);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(body);
        Content = root;

        LoadPlaces();

        Opened += (_, _) =>
        {
            RecolourSwatches();
            UpdateZoomButtons();
        };
        Closed += (_, _) => _closed = true;
        KeyDown += OnMapKeyDown;
    }

    /// <summary>The map, for tests.</summary>
    internal MapCanvas Canvas => _canvas;

    internal ArtifactBrowser Artifacts => _artifactBrowser;

    internal ListBox PassageList => _passageList;

    internal IReadOnlyList<PlacePassage> Passages => _currentPassages;

    internal string ResultsStatus => _resultsStatus.Text ?? string.Empty;

    /// <summary>The click being answered, for tests to wait on.</summary>
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Plural, readable label for a kind. Region covers islands and countries
    /// as well, so it is worded for what it holds rather than named after the
    /// enum.
    /// </summary>
    private static string KindLabel(PlaceKind kind) => kind switch
    {
        PlaceKind.City => "Cities",
        PlaceKind.Sanctuary => "Sanctuaries",
        PlaceKind.Battlefield => "Battlefields",
        PlaceKind.Water => "Rivers and seas",
        _ => "Regions and islands"
    };

    private static string KindSingular(PlaceKind kind) => kind switch
    {
        PlaceKind.City => "City",
        PlaceKind.Sanctuary => "Sanctuary",
        PlaceKind.Battlefield => "Battlefield",
        PlaceKind.Water => "River, sea or lake",
        _ => "Region or island"
    };

    private void LoadPlaces()
    {
        var markers = PlaceData.All()
            .Select(p => new MapCanvas.PlaceMarker { Name = p.Name, Lat = p.Lat, Lon = p.Lon, Kind = p.Kind })
            .ToList();

        _canvas.SetData(markers);
    }

    /// <summary>Selects a place and lists what the library has for it, as a click on its pin does.</summary>
    internal Task ShowPlaceAsync(string placeName)
    {
        var marker = _canvas.Markers.FirstOrDefault(m => string.Equals(m.Name, placeName, StringComparison.OrdinalIgnoreCase));
        if (marker == null) return Task.CompletedTask;

        _canvas.Selected = marker;
        return PendingLoad = ShowPlaceAsync(marker);
    }

    private async Task ShowPlaceAsync(MapCanvas.PlaceMarker marker)
    {
        var request = ++_request;

        _placeTitle.Text = marker.Name;
        _placeTitle.IsVisible = true;
        _placeDetail.Text = $"{KindSingular(marker.Kind)} · {Degrees(marker.Lat, "N", "S")}, {Degrees(marker.Lon, "E", "W")}";

        try
        {
            await LoadArtifactsAsync(marker.Name, request);
            await LoadSearchResultsAsync(marker.Name, request);
        }
        catch (Exception ex)
        {
            if (_closed || request != _request) return;
            _resultsStatus.Text = string.Empty;
            await ShowErrorAsync("Couldn't search the library", ex);
        }
    }

    private static string Degrees(double value, string positive, string negative) =>
        $"{Math.Abs(value):0.00}° {(value >= 0 ? positive : negative)}";

    private async Task LoadArtifactsAsync(string placeName, int request)
    {
        var artifacts = await _artifactRepo.GetByPlaceNameAsync(placeName);
        if (_closed || request != _request) return;

        // No objects at a place and no catalogue to look in are different
        // answers; only the second has something the reader can do about it.
        if (artifacts.Count == 0)
        {
            _hasCatalogue ??= await _artifactRepo.HasDataAsync();
            if (_closed || request != _request) return;
        }

        _artifactBrowser.EmptyNote = _hasCatalogue == false ? ArtifactBrowser.MissingCatalogueNote : null;
        _artifactBrowser.LoadArtifacts(artifacts);
    }

    /// <summary>
    /// Runs the same word-form-aware search the main search box uses, over the
    /// place's literal name.
    /// </summary>
    private async Task LoadSearchResultsAsync(string placeName, int request)
    {
        _selectedPlaceName = placeName;
        _resultsLabel.Text = $"Search results for “{placeName}” (double-click to jump):";
        _resultsLabel.IsVisible = true;
        _currentPassages = new List<PlacePassage>();
        _passageList.ItemsSource = null;
        _resultsStatus.Text = $"Searching the library for “{placeName}”…";

        // Off the UI thread, so the map keeps answering while the corpus is
        // searched - on it, a click stopped the map responding until the search
        // finished.
        //
        // By word, not by substring. A raw LIKE '%name%' matches the letters
        // wherever they fall, and a short place name falls inside ordinary words
        // constantly: clicking Ur returned five thousand passages of which
        // exactly one mentioned Ur, the rest being "during", "figure", "purple"
        // and the like. Measured over all 240 places on the map, 57 of them
        // returned more noise than mentions - 86,727 passages that had nothing
        // to do with the place clicked. SearchPhraseAsync resolves through the
        // word index, which holds whole normalized words, so it matches the name
        // and not the letters; the same measurement afterwards gave 603.
        //
        // Phrase, not forms. A forms search took every name with a space in it
        // to nothing at all: normalization strips the space, so "Euxine sea"
        // reached the index as the single token "euxinesea", and eight pins -
        // the Euxine sea, the Arabian Gulf, Egyptian Thebes, lake Moeris, Hippo
        // Regius, Colonia Agrippina, Monte Cassino, Boeotian Thebes - answered a
        // click with an empty list.
        //
        // Both queries inside the one Task.Run. The tag lookup is the more
        // expensive of the two by a wide margin, and left on the UI thread it
        // stalled a click for a couple of seconds after the search had stopped
        // being the reason.
        var (hits, tagsByNode) = await Task.Run(async () =>
        {
            var found = await _textNodeRepo.SearchPhraseAsync(placeName);
            var tags = await _tagRepo.GetTagNamesForNodesAsync(found.Rows.Select(p => p.TextNodeId).ToList());
            return (found, tags);
        });

        // A reader who clicks a pin and closes the window, or clicks another
        // pin, before this answers has moved on; the answer is dropped.
        if (_closed || request != _request) return;

        _currentPassages = hits.Rows
            .Select(r => new PlacePassage(r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text, r.Milestone,
                tagsByNode.TryGetValue(r.TextNodeId, out var tags) ? tags : (IReadOnlyList<string>)Array.Empty<string>()))
            .ToList();
        _truncated = hits.Truncated;

        RenderPassageList();
    }

    private void RenderPassageList()
    {
        _passageList.ItemsSource = _currentPassages;

        _resultsStatus.Text = _currentPassages.Count == 0
            ? "No passages found."
            : _truncated
                ? $"Stopped at {_currentPassages.Count:N0}; there are more."
                : _currentPassages.Count == 1 ? "1 passage" : $"{_currentPassages.Count:N0} passages";
    }

    private Control BuildPassageRow(PlacePassage passage)
    {
        var stack = new StackPanel { Spacing = 3, Margin = new Thickness(2, 5, 2, 6) };

        stack.Children.Add(new TextBlock
        {
            Text = $"{passage.AuthorName}, {passage.WorkTitle}",
            Classes = { "caption" },
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // A passage you have already marked, met again from the map side. The
        // tags didn't produce this list and aren't a filter on it - they are
        // worth noticing where they happen to coincide with it, which is the
        // only thing tags were ever doing on this screen.
        if (passage.Tags.Count > 0)
        {
            var tagIcon = new PathIcon { Data = Glyphs.Get("Tags"), Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center };
            var tagText = new TextBlock
            {
                Text = $"tagged: {string.Join(", ", passage.Tags)}",
                Classes = { "caption" },
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            tagIcon.Bind(PathIcon.ForegroundProperty, tagIcon.GetResourceObservable("Cc.Accent"));
            tagText.Bind(TextBlock.ForegroundProperty, tagText.GetResourceObservable("Cc.Accent"));
            stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { tagIcon, tagText } });
        }

        // Trimmed rather than scrolled. The whole passage is one double-click
        // away in the reader, where it is set in a font chosen for it.
        var text = new TextBlock
        {
            Text = ListResultText(passage.Text),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.WordEllipsis,
            MaxLines = 3,
            FontSize = 13.5
        };
        if (Application.Current?.FindResource("Cc.SourceFont") is FontFamily font) text.FontFamily = font;
        stack.Children.Add(text);

        // The reference on hover, as every result list in the app keeps it:
        // available, without printing it into every row.
        var citation = PassageCitation.Bracketed(passage.CitationRef, passage.Milestone);
        if (citation.Length > 0) ToolTip.SetTip(stack, citation);

        return stack;
    }

    /// <summary>
    /// As much of a passage as three lines can use. The cut keeps the layout
    /// from measuring the 30,000-character sections a few Migne works were
    /// ingested as; nothing is lost by it, since the copy and the export read
    /// the whole text from the passage itself.
    /// </summary>
    private static string ListResultText(string text)
    {
        const int limit = 400;
        if (text.Length <= limit) return text;

        var cut = limit;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    private async Task JumpToSelectedPassageAsync()
    {
        if (_passageList.SelectedItem is not PlacePassage passage) return;

        // The map stays open: it sits beside the reader rather than in front of
        // it, so the next passage for the same place is one double-click away.
        await NavigateAsync(passage.WorkId, passage.TextNodeId);
    }

    /// <summary>
    /// Open, copy, and the table exports. The exports take the whole gathered
    /// set rather than the visible rows, so a status line can never end up in
    /// a spreadsheet.
    /// </summary>
    private void AttachPassageMenu()
    {
        var open = new MenuItem { Header = "Open in Reader", Icon = Glyphs.Icon("Work", 14) };
        open.Click += async (_, _) => await JumpToSelectedPassageAsync();

        var copy = new MenuItem { Header = "Copy to Clipboard", Icon = Glyphs.Icon("Copy", 14) };
        copy.Click += async (_, _) =>
        {
            if (_passageList.SelectedItem is PlacePassage p)
                await ClipboardText.SetAsync(this, $"{p.AuthorName}, {p.WorkTitle} [{PassageCitation.Display(p.CitationRef, p.Milestone)}]: {p.Text}");
        };

        var menu = new ContextMenu();
        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.Opening += (_, _) =>
        {
            var any = _passageList.SelectedItem is PlacePassage;
            open.IsEnabled = any;
            copy.IsEnabled = any;
        };
        _passageList.ContextMenu = menu;

        IReadOnlyList<string> Header() => new[] { "Author", "Work", "Passage", "Text", "Your tags" };

        IReadOnlyList<string> RowFor(PlacePassage p) => new[]
        {
            p.AuthorName, p.WorkTitle, PassageCitation.Display(p.CitationRef, p.Milestone), p.Text, string.Join(", ", p.Tags)
        };

        TableExport.Attach(
            _passageList,
            () => $"Passages mentioning {_selectedPlaceName}",
            () => new[] { Header() }.Concat(_currentPassages.Select(RowFor)).ToList(),
            () => _passageList.SelectedItem is PlacePassage p ? new[] { Header(), RowFor(p) } : new[] { Header() },
            () =>
            {
                var notes = new List<string>
                {
                    $"Passages mentioning {_selectedPlaceName}, found by searching the library for the name as a word or phrase (Places Map)."
                };
                if (_truncated) notes.Add($"The search stopped at {_currentPassages.Count:N0}; there are more.");
                return notes;
            });
    }

    /// <summary>
    /// The swatches take their colours from the canvas, so the key showing what
    /// each colour means can never drift from what the pins are drawn in.
    /// </summary>
    private void RecolourSwatches()
    {
        foreach (var (kind, swatch) in _kindSwatches)
        {
            swatch.Fill = new SolidColorBrush(_canvas.LegendColorFor(kind));
            swatch.Stroke = new SolidColorBrush(_canvas.LegendOutlineFor(kind));
        }
    }

    private void UpdateZoomButtons()
    {
        _zoomIn.IsEnabled = _canvas.CanZoomIn;
        _zoomOut.IsEnabled = _canvas.CanZoomOut;
        _zoomHome.IsEnabled = _canvas.CanZoomOut;
    }

    /// <summary>⌘+, ⌘− and ⌘0, as Maps and Preview have them.</summary>
    private void OnMapKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!command) return;

        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                _canvas.ZoomBy(1.6);
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract:
                _canvas.ZoomBy(1 / 1.6);
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0:
                _canvas.ResetView();
                e.Handled = true;
                break;
        }
    }

    private static Button ZoomButton(string glyph, string tip, Action action)
    {
        var button = new Button
        {
            Classes = { "toolbar" },
            Content = new PathIcon { Data = Glyphs.Get(glyph), Width = 14, Height = 14 },
            Width = 32,
            Height = 30
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private static void Row(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }
}
