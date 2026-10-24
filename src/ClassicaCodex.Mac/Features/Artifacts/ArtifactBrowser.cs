using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Artifacts;

/// <summary>
/// Browses a list of artifacts: a category filter, a live-loaded IIIF image,
/// description text, and Previous/Next paging. The Places Map holds one in
/// its own layout and the Myth Network opens one in a window of its own
/// (ArtifactBrowserWindow) - the same browsing for two different kinds of
/// lookup, kept as one control because two copies would drift apart.
///
/// Callers fetch the artifact list themselves however makes sense for them (a
/// place match, a text search) and hand it to <see cref="LoadArtifacts"/>;
/// this control only displays and pages through what it is given, and does
/// not know or care where the list came from.
///
/// The photographs are never part of the library. Perseus's terms for the
/// Art &amp; Archaeology collection do not allow its images to be
/// redistributed outside its own site, so the catalogue's descriptions are
/// stored and every photograph is fetched from Perseus's IIIF server at the
/// moment it is looked at - held in memory while this control lives, never
/// written to disk. The control says so beneath the photograph, with the
/// photograph's own credit, so the reader knows why a picture needs a
/// connection when the text beside it does not.
/// </summary>
public sealed class ArtifactBrowser : UserControl
{
    /// <summary>
    /// Shown, in place of an empty browser, when the library has no catalogue
    /// at all - so "no objects here" is not mistaken for a fact about the place.
    /// </summary>
    public const string MissingCatalogueNote =
        "The Art & Archaeology catalogue isn't in this library. Add “Art & Archaeology Data (Perseus)” from Setup " +
        "to see the objects found here - only their descriptions are stored; the photographs always load live from Perseus.";

    private const string ConditionsText =
        "Loaded live from Perseus's server as you look at it and never saved to this computer: Perseus's terms " +
        "for this collection don't permit redistributing its images.";

    // One HTTP request per photograph shown, through one client for the life
    // of the app - the standard guidance for this type, since each instance
    // holds its own connection pool and one per request can exhaust sockets
    // under rapid paging.
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const int MaxCachedImages = 60;

    private readonly ArtifactRepository _artifactRepo = new();

    private readonly Grid _details;
    private readonly StackPanel _compact;
    private readonly PathIcon _compactGlyph;
    private readonly TextBlock _compactTitle;
    private readonly TextBlock _emptyNote;
    private readonly ComboBox _categoryFilter;
    private readonly TextBlock _header;
    private readonly Border _frame;
    private readonly Image _image;
    private readonly StackPanel _frameMessage;
    private readonly PathIcon _frameGlyph;
    private readonly TextBlock _frameText;
    private readonly SelectableTextBlock _caption;
    private readonly TextBlock _photoHint;
    private readonly TextBlock _credit;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly TextBlock _conditions;

    /// <summary>
    /// Bytes of photographs fetched while this browser is open, so paging back
    /// and forth through a place's objects doesn't go back to Perseus for
    /// something shown a second ago.
    ///
    /// In memory only: transient display state, the same thing a browser
    /// holds while you look at a page, not a stored copy. Nothing is written to
    /// disk, which is the line Perseus's terms draw. Bytes rather than decoded
    /// bitmaps, because a bitmap handed out of a cache has murky ownership -
    /// the Image would dispose something the cache still holds - while bytes
    /// are immutable and a fresh bitmap is cheap. The network is the expensive
    /// part being avoided, not the decode.
    /// </summary>
    private readonly Dictionary<string, byte[]> _imageBytesCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _imageCacheOrder = new();

    private List<Artifact> _allArtifacts = new();
    private List<Artifact> _currentArtifacts = new();
    private int _currentIndex;

    /// <summary>Every photo of the artifact on screen, and which of them is showing.</summary>
    private List<ArtifactImage> _currentImages = new();
    private int _imageIndex;

    /// <summary>
    /// Counts renders, so a photograph that arrives after the reader has
    /// already moved on is dropped rather than shown beside the wrong
    /// object's description.
    /// </summary>
    private int _renderGeneration;
    private CancellationTokenSource? _fetch;
    private Bitmap? _bitmap;
    private bool _populatingFilter;

    public ArtifactBrowser()
    {
        _categoryFilter = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<CategoryFilterOption>((option, _) => new TextBlock { Text = option?.ToString() }),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _categoryFilter.SelectionChanged += async (_, _) =>
        {
            if (_populatingFilter) return;
            ApplyCategoryFilter();
            await RenderCurrentArtifactAsync();
        };

        _header = new TextBlock { Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };

        _compactGlyph = new PathIcon { Data = Glyphs.Get("Image"), Width = 36, Height = 36, Opacity = 0.35, Margin = new Thickness(0, 0, 0, 6) };
        _compactTitle = new TextBlock { Text = "No artifacts to show yet.", Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };
        _emptyNote = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
        _compact = new StackPanel { Children = { _compactGlyph, _compactTitle, _emptyNote } };

        _image = new Image { Stretch = Stretch.Uniform };

        _frameGlyph = new PathIcon { Data = Glyphs.Get("Image"), Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.5 };
        _frameText = new TextBlock
        {
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 240
        };
        _frameMessage = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12),
            Children = { _frameGlyph, _frameText }
        };

        _frame = new Border
        {
            Classes = { "panel" },
            Height = 170,
            Margin = new Thickness(0, 8, 0, 0),
            ClipToBounds = true,
            Child = new Grid { Children = { _frameMessage, _image } }
        };

        // Clicking the photo steps to the next photo OF THE SAME OBJECT.
        // Previous and Next below step between objects; Perseus photographs
        // most things several times over, and without this there would be no
        // way to see any view but the first.
        _frame.Tapped += async (_, _) =>
        {
            if (_currentImages.Count < 2) return;
            _imageIndex = (_imageIndex + 1) % _currentImages.Count;
            await RenderCurrentArtifactAsync();
        };

        _caption = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.WordEllipsis,
            MaxLines = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Classes = { "muted" }
        };

        _photoHint = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        _credit = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic, Margin = new Thickness(0, 4, 0, 0) };

        _prevButton = PagingButton("Previous", "Back", glyphFirst: true);
        _prevButton.Click += async (_, _) =>
        {
            if (_currentArtifacts.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + _currentArtifacts.Count) % _currentArtifacts.Count;
            _imageIndex = 0; // a different object, so back to its first photo
            await RenderCurrentArtifactAsync();
        };

        _nextButton = PagingButton("Next", "Forward", glyphFirst: false);
        _nextButton.Click += async (_, _) =>
        {
            if (_currentArtifacts.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _currentArtifacts.Count;
            _imageIndex = 0;
            await RenderCurrentArtifactAsync();
        };

        var paging = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0), Children = { _prevButton, _nextButton } };
        _prevButton.Margin = new Thickness(0, 0, 4, 0);
        _nextButton.Margin = new Thickness(4, 0, 0, 0);

        _conditions = new TextBlock { Text = ConditionsText, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.85 };

        _details = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto") };
        Place(_details, _categoryFilter, 0);
        Place(_details, _header, 1);
        Place(_details, _frame, 2);
        Place(_details, _caption, 3);
        Place(_details, _photoHint, 4);
        Place(_details, _credit, 5);
        Place(_details, paging, 6);
        Place(_details, _conditions, 7);

        Content = new Panel { Children = { _details, _compact } };

        ImagesFor = id => _artifactRepo.GetImagesForArtifactAsync(id);
        ShowCompact("No artifacts to show yet.");
    }

    /// <summary>
    /// The photograph's height: a fixed number where the browser shares a
    /// column with other things (the Places Map), NaN to take whatever room
    /// its own window gives it.
    /// </summary>
    public double PhotoHeight
    {
        get => _frame.Height;
        set
        {
            _frame.Height = value;
            if (_compact.IsVisible) ShowCompact(_compactTitle.Text ?? string.Empty);
        }
    }

    /// <summary>How many lines of description show before it is cut; the whole text is in its tooltip.</summary>
    public int CaptionLines
    {
        get => _caption.MaxLines;
        set => _caption.MaxLines = value;
    }

    /// <summary>
    /// A line under "No linked artifacts found." explaining why - set to
    /// <see cref="MissingCatalogueNote"/> when the library has no catalogue.
    /// </summary>
    public string? EmptyNote { get; set; }

    /// <summary>Where an object's photographs are looked up: the library, unless a test supplies them.</summary>
    internal Func<string, Task<List<ArtifactImage>>> ImagesFor { get; set; }

    /// <summary>
    /// Replaces the fetch from Perseus, so a test can stand in for the network
    /// - and for its absence - without depending on reaching it.
    /// </summary>
    internal static Func<string, CancellationToken, Task<byte[]>>? FetchOverride { get; set; }

    /// <summary>The render in progress, for tests to wait on.</summary>
    internal Task PendingRender { get; private set; } = Task.CompletedTask;

    internal string HeaderText => (_compact.IsVisible ? _compactTitle.Text : _header.Text) ?? string.Empty;

    internal string EmptyNoteText => _emptyNote.IsVisible ? _emptyNote.Text ?? string.Empty : string.Empty;

    internal string FrameMessage => _frameMessage.IsVisible ? _frameText.Text ?? string.Empty : string.Empty;

    internal string CaptionText => _caption.Text ?? string.Empty;

    internal string CreditText => _credit.IsVisible ? _credit.Text ?? string.Empty : string.Empty;

    internal string ConditionsLine => _conditions.IsVisible ? _conditions.Text ?? string.Empty : string.Empty;

    internal bool HasPhoto => _bitmap != null;

    /// <summary>Whatever list the caller fetched - a place match, a text search, an empty list if nothing was found.</summary>
    public void LoadArtifacts(List<Artifact> artifacts)
    {
        _allArtifacts = artifacts;
        PopulateCategoryFilter();

        // Applied here rather than left to the filter's selection event, which
        // does not fire when a new list lands on the index the previous one was
        // already on - and index 0 is where every list lands.
        ApplyCategoryFilter();
        PendingRender = RenderCurrentArtifactAsync();
    }

    /// <summary>One "All categories" option plus one per type actually present, each with its count.</summary>
    private void PopulateCategoryFilter()
    {
        var options = new List<CategoryFilterOption> { new(null, _allArtifacts.Count) };
        options.AddRange(_allArtifacts
            .GroupBy(a => a.Type)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CategoryFilterOption(g.Key, g.Count())));

        _populatingFilter = true;
        try
        {
            _categoryFilter.ItemsSource = options;
            _categoryFilter.SelectedIndex = 0;
        }
        finally
        {
            _populatingFilter = false;
        }
    }

    private void ApplyCategoryFilter()
    {
        var selected = _categoryFilter.SelectedItem as CategoryFilterOption;
        _currentArtifacts = selected?.Category == null
            ? _allArtifacts
            : _allArtifacts.Where(a => a.Type == selected.Category).ToList();
        _currentIndex = 0;
        _imageIndex = 0;
    }

    /// <summary>One entry in the category filter - a null Category means "All categories".</summary>
    private sealed class CategoryFilterOption
    {
        public CategoryFilterOption(string? category, int count)
        {
            Category = category;
            Count = count;
        }

        public string? Category { get; }

        private int Count { get; }

        public override string ToString() => Category == null ? $"All categories ({Count})" : $"{Category} ({Count})";
    }

    /// <summary>
    /// Renders whichever artifact the index points at. The photograph is
    /// loaded live from Perseus's own IIIF server every time this runs (or
    /// from this session's memory of it), never from disk, per Perseus's
    /// terms. An artifact with no linked photo still shows its description;
    /// only the picture area goes without.
    /// </summary>
    private async Task RenderCurrentArtifactAsync()
    {
        var generation = ++_renderGeneration;
        _fetch?.Cancel();
        SetBitmap(null);

        if (_currentArtifacts.Count == 0)
        {
            ShowCompact("No linked artifacts found.");
            return;
        }

        ShowDetails();

        var artifact = _currentArtifacts[_currentIndex];
        _header.Text = $"{artifact.Type} - {artifact.Name ?? artifact.ArtifactId} ({_currentIndex + 1} of {_currentArtifacts.Count})";
        _prevButton.IsEnabled = _currentArtifacts.Count > 1;
        _nextButton.IsEnabled = _currentArtifacts.Count > 1;

        // Every photo of this object, not just the first. Perseus supplies
        // several views of most things it has photographed - the full catalogue
        // holds 45,688 images across 4,754 objects, nine on average and 2,586
        // for one of them - and showing only the first would leave nine in ten
        // of them unreachable.
        List<ArtifactImage> images;
        try
        {
            images = await ImagesFor(artifact.ArtifactId);
        }
        catch (Exception)
        {
            images = new List<ArtifactImage>();
        }

        if (generation != _renderGeneration) return;

        _currentImages = images;
        if (_imageIndex >= _currentImages.Count) _imageIndex = 0;

        var image = _currentImages.Count > 0 ? _currentImages[_imageIndex] : null;

        var caption = !string.IsNullOrWhiteSpace(artifact.Description)
            ? artifact.Description!
            : image?.Caption ?? "(no description available)";

        if (image == null)
        {
            _caption.Text = caption + "\n(no photo available for this object)";
            ToolTip.SetTip(_caption, _caption.Text);
            ShowFrameMessage("No photograph of this object", "Image");
            _photoHint.IsVisible = false;
            _credit.IsVisible = false;
            _conditions.IsVisible = false;
            _frame.Cursor = Cursor.Default;
            return;
        }

        _caption.Text = caption;
        ToolTip.SetTip(_caption, caption);

        // Clicking the photo is how the rest are reached. Previous and Next
        // already mean the previous and next OBJECT, and giving them a second
        // meaning would be worse than the extra click.
        if (_currentImages.Count > 1)
        {
            _photoHint.Text = $"Photo {_imageIndex + 1} of {_currentImages.Count} - click the photo for the next";
            _photoHint.IsVisible = true;
            _frame.Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            _photoHint.IsVisible = false;
            _frame.Cursor = Cursor.Default;
        }

        // The credit is the photograph's own, as Perseus recorded it, not a
        // summary of it.
        _credit.Text = string.IsNullOrWhiteSpace(image.Credits)
            ? "Photograph from the Perseus Digital Library."
            : $"Photograph: {image.Credits!.Trim()}";
        _credit.IsVisible = true;
        _conditions.IsVisible = true;

        ShowFrameMessage("Loading the photograph from Perseus…", "Download");

        var fetch = new CancellationTokenSource();
        _fetch = fetch;

        try
        {
            var bytes = await GetImageBytesAsync(image.ImageId, fetch.Token);
            if (generation != _renderGeneration) return;

            using var stream = new MemoryStream(bytes);
            SetBitmap(new Bitmap(stream));
            _frameMessage.IsVisible = false;
        }
        catch (Exception) when (generation == _renderGeneration)
        {
            // A network hiccup, Perseus's server briefly away, or no connection
            // at all. The description beside it is still useful on its own, so
            // this stays quiet - a word in the picture's place, not an error for
            // something this minor.
            ShowFrameMessage("Couldn't reach Perseus for this photograph. It loads live each time it is viewed, so it needs a connection.", "Warning");
        }
        catch (Exception)
        {
            // Superseded by a later render while it was in flight: whatever it
            // was, it is no longer what the reader is looking at.
        }
    }

    /// <summary>
    /// The image bytes, from this session's memory when already fetched.
    /// Eviction is plain first-in-first-out rather than least-recently-used:
    /// browsing here is overwhelmingly sequential (Next, Next, Next, then back a
    /// few), so insertion order and recency are near enough the same, and FIFO
    /// needs no bookkeeping on every hit.
    /// </summary>
    private async Task<byte[]> GetImageBytesAsync(string imageId, CancellationToken cancellationToken)
    {
        if (_imageBytesCache.TryGetValue(imageId, out var cached)) return cached;

        var url = BuildIiifThumbnailUrl(imageId);
        var bytes = FetchOverride != null
            ? await FetchOverride(url, cancellationToken)
            : await s_httpClient.GetByteArrayAsync(url, cancellationToken);

        _imageBytesCache[imageId] = bytes;
        _imageCacheOrder.Enqueue(imageId);

        while (_imageCacheOrder.Count > MaxCachedImages)
            _imageBytesCache.Remove(_imageCacheOrder.Dequeue());

        return bytes;
    }

    /// <summary>Perseus's own IIIF pattern, confirmed against a live coin image and a live vase image - both resolved at pct:50.</summary>
    internal static string BuildIiifThumbnailUrl(string imageId) =>
        $"https://iiif.perseus.tufts.edu/iiif/3/{imageId}/full/pct:50/0/default.png";

    /// <summary>
    /// Only the sentence, when there is nothing to page through: an empty
    /// photograph frame and two disabled buttons would take a third of the
    /// Places Map's column to say "nothing here". In a window of its own the
    /// sentence sits in the middle, under a picture of the picture it lacks.
    /// </summary>
    private void ShowCompact(string message)
    {
        var ownWindow = double.IsNaN(PhotoHeight);

        _compactTitle.Text = message;
        _emptyNote.Text = EmptyNote;
        _emptyNote.IsVisible = !string.IsNullOrWhiteSpace(EmptyNote);

        _compactGlyph.IsVisible = ownWindow;
        _compact.VerticalAlignment = ownWindow ? VerticalAlignment.Center : VerticalAlignment.Top;
        _compact.HorizontalAlignment = ownWindow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _compact.MaxWidth = ownWindow ? 320 : double.PositiveInfinity;
        _compact.Margin = ownWindow ? new Thickness(0, 0, 0, 40) : default;
        var alignment = ownWindow ? TextAlignment.Center : TextAlignment.Left;
        _compactTitle.TextAlignment = alignment;
        _emptyNote.TextAlignment = alignment;

        _compact.IsVisible = true;
        _details.IsVisible = false;
        _currentImages = new List<ArtifactImage>();
    }

    private void ShowDetails()
    {
        _compact.IsVisible = false;
        _details.IsVisible = true;
        foreach (var control in _details.Children) control.IsVisible = true;
    }

    private void ShowFrameMessage(string text, string glyph)
    {
        _frameText.Text = text;
        _frameGlyph.Data = Glyphs.Get(glyph);
        _frameMessage.IsVisible = true;
    }

    private void SetBitmap(Bitmap? bitmap)
    {
        var old = _bitmap;
        _bitmap = bitmap;
        _image.Source = bitmap;
        old?.Dispose();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // The window has gone: nothing is waiting for the photograph, and the
        // decoded one is let go with it.
        _renderGeneration++;
        _fetch?.Cancel();
        SetBitmap(null);
    }

    private static Button PagingButton(string label, string glyph, bool glyphFirst)
    {
        var icon = new PathIcon { Data = Glyphs.Get(glyph), Width = 12, Height = 12 };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyphFirst)
        {
            content.Children.Add(icon);
            content.Children.Add(text);
        }
        else
        {
            content.Children.Add(text);
            content.Children.Add(icon);
        }

        return new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = false
        };
    }

    private static void Place(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }
}
