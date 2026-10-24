using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Ingestion.Catmus;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.MedievalHands;

/// <summary>
/// The leaves of a manuscript, from the library that holds it - the Windows
/// ManuscriptPagesForm.
///
/// <b>What this adds to Medieval Hands.</b> That window shows a line cropped
/// out of a page, which is what CATMuS records; this shows the page it was
/// cropped out of - the ruling, the columns, the rubrics, the decorated
/// initials, the marginalia, and whatever illumination the book has. The
/// transcription tells you what the scribe wrote; this is the object.
///
/// <b>Nothing is downloaded.</b> Each leaf is fetched from the library's own
/// IIIF service when it is looked at and kept only while it is on screen -
/// the same rule the Art &amp; Archaeology browser follows for Perseus, and
/// for the same reason: these photographs belong to the library, are
/// published under its terms and not ours, and the terms differ by library.
/// Whatever the manifest says about credit and conditions is shown here
/// rather than assumed.
///
/// Modeless on the Mac, beside Medieval Hands rather than in front of it, so
/// a line and the leaf it came from can be looked at together. Every fetch
/// runs off the UI thread and is cancelled when another leaf is chosen or the
/// window closes.
/// </summary>
public sealed class ManuscriptPagesWindow : FeatureWindow
{
    private readonly string _shelfmark;
    private readonly string _manifestUrl;

    private readonly ListBox _pageList;
    private readonly ManuscriptImageView _page;
    private readonly ComboBox _zoom;
    private readonly TextBlock _zoomPercent;
    private readonly TextBlock _status;
    private readonly SelectableTextBlock _terms;
    private readonly StackPanel _links;

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _sharpen;
    private IiifManifest? _manifest;

    /// <summary>
    /// Cancels the leaf being fetched when another is chosen. Someone running
    /// down the page list with the arrow keys starts a request per keypress,
    /// and without this the one that happens to finish last wins - so the
    /// picture on screen ends up belonging to a leaf that is no longer
    /// selected.
    ///
    /// Cancelled but not disposed. The request being cancelled is still
    /// inside an await and will look at its own token on the way out;
    /// disposing the source underneath it is asking for trouble for the sake
    /// of an object that holds no timer and no registrations.
    /// </summary>
    private CancellationTokenSource? _loading;

    /// <summary>The width the leaf on screen was asked for, and which leaf it is.</summary>
    private int _loadedRequest;

    private int _loadedIndex = -1;

    private bool _settingZoom;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    public ManuscriptPagesWindow(string shelfmark, string manifestUrl)
        : this(shelfmark, manifestUrl, handler: null)
    {
    }

    /// <summary>With a message handler of the caller's, so the tests can be the library.</summary>
    internal ManuscriptPagesWindow(string shelfmark, string manifestUrl, HttpMessageHandler? handler)
    {
        _shelfmark = shelfmark;
        _manifestUrl = manifestUrl;

        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);

        Title = $"Pages - {shelfmark}";
        Width = 980;
        Height = 780;
        MinWidth = 560;
        MinHeight = 420;

        _page = new ManuscriptImageView { Zoom = ManuscriptImageZoom.FitAll, FitPreset = ManuscriptImageZoom.FitAll };
        _page.ZoomChanged += (_, _) => OnZoomChanged();

        // --- toolbar ---------------------------------------------------
        var previous = NavButton("Back", "Previous leaf (←)", () => Step(-1));
        var next = NavButton("Forward", "Next leaf (→)", () => Step(1));
        _status = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        _zoom = new ComboBox
        {
            Width = 136,
            FontSize = 12,
            MinHeight = 26,
            Padding = new Thickness(8, 2),
            PlaceholderText = "Zoomed",
            ItemsSource = new[] { "Fit the page", "Fit the width", "50%", "100%", "200%" },
            SelectedIndex = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        _zoom.SelectionChanged += (_, _) =>
        {
            if (_settingZoom || _zoom.SelectedIndex < 0) return;
            _page.Zoom = _zoom.SelectedIndex switch
            {
                0 => ManuscriptImageZoom.FitAll,
                1 => ManuscriptImageZoom.FitWidth,
                2 => ManuscriptImageZoom.Half,
                3 => ManuscriptImageZoom.Actual,
                _ => ManuscriptImageZoom.Double
            };

            // A bigger zoom wants a bigger fetch: asking the library for a
            // thousand pixels and then magnifying them to two thousand shows
            // the JPEG, not the parchment.
            if (NeedsFetch()) _pending = ShowSelectedPageAsync();
        };
        _zoomPercent = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Right };

        var zoomOut = NavButton("Minus", "Zoom out (⌘-)", () => _page.ZoomOut());
        var zoomIn = NavButton("Plus", "Zoom in (⌘+)", () => _page.ZoomIn());
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _zoomPercent, zoomOut, zoomIn, _zoom } };

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { previous, next } };
        DockPanel.SetDock(navigation, Dock.Left);
        DockPanel.SetDock(zoomRow, Dock.Right);
        toolbar.Children.Add(navigation);
        toolbar.Children.Add(zoomRow);
        toolbar.Children.Add(_status);

        // --- body ------------------------------------------------------
        _pageList = new ListBox { SelectionMode = SelectionMode.Single, Padding = new Thickness(4, 4) };
        ScrollViewer.SetHorizontalScrollBarVisibility(_pageList, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _pageList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 4)),
                new Setter(ListBoxItem.MinHeightProperty, 0d),
                new Setter(ListBoxItem.FontSizeProperty, 12.5)
            }
        });
        _pageList.SelectionChanged += (_, _) => _pending = ShowSelectedPageAsync();

        // A pinch that has come to rest past what the leaf on screen can show
        // asks for a sharper one - after a pause rather than on every step of
        // the gesture, because each fetch is a request to someone else's
        // server and Gallica, for one, rate-limits.
        _sharpen = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _sharpen.Tick += (_, _) =>
        {
            _sharpen.Stop();
            if (NeedsFetch()) _pending = ShowSelectedPageAsync(keepView: true);
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("170,12,*") };
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        var pagesPane = ToolPane.Build("LEAVES", _pageList);
        var leaf = new Border { Classes = { "page" }, Child = _page };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(leaf, 2);
        body.Children.Add(pagesPane);
        body.Children.Add(splitter);
        body.Children.Add(leaf);

        // --- credit and conditions ---------------------------------------
        _terms = new SelectableTextBlock { Text = "Loading...", TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        _links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 6, 0, 0) };
        var footer = new StackPanel { Margin = new Thickness(2, 12, 2, 0), Children = { _terms, _links } };

        var layout = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(toolbar);
        layout.Children.Add(footer);
        layout.Children.Add(body);
        Content = layout;

        KeyDown += (_, e) =>
        {
            if (_page.TryHandleZoomKey(e))
            {
                e.Handled = true;
            }
            else if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None)
            {
                Step(e.Key == Key.Left ? -1 : 1);
                e.Handled = true;
            }
        };

        // Whatever is still in flight is cancelled and finds the window closed
        // on its way out; the leaf on screen goes with the view.
        Closed += (_, _) =>
        {
            _closed = true;
            _sharpen.Stop();
            _lifetime.Cancel();
            _loading?.Cancel();
            _http.Dispose();
        };

        Opened += (_, _) =>
        {
            MedievalHandsWindow.FitToScreen(this);
            _pending = LoadManifestAsync();
        };
    }

    // For the tests.
    internal ListBox PageList => _pageList;
    internal ManuscriptImageView Page => _page;
    internal ComboBox ZoomBox => _zoom;
    internal string StatusText => _status.Text ?? string.Empty;
    internal string TermsText => _terms.Text ?? string.Empty;
    internal IReadOnlyList<(string Label, string Url)> Links =>
        _links.Children.OfType<Button>().Select(b => ((string)b.Tag!, ToolTip.GetTip(b) as string ?? string.Empty)).ToList();
    internal int LoadedRequest => _loadedRequest;

    internal async Task WhenIdleAsync()
    {
        Task current;
        do
        {
            current = _pending;
            await current;
        }
        while (!ReferenceEquals(current, _pending));
    }

    private async Task LoadManifestAsync()
    {
        _status.Text = "Asking the library...";

        try
        {
            _manifest = await Task.Run(() => IiifManifest.LoadAsync(_http, _manifestUrl, _lifetime.Token));
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _status.Text = "The library could not be reached.";
            _terms.Text = $"{_shelfmark}: {Explain(ex)}";
            _page.Show(null, "The library could not be reached.", Art.Tile("medieval-hands", "MedievalHands", 64));
            return;
        }

        if (_closed) return;

        if (_manifest.Pages.Count == 0)
        {
            _status.Text = "No pages.";
            _terms.Text = $"{_shelfmark}: the library published a record for this manuscript but no page images " +
                          "this application can read.";
            _page.Show(null, "No pages.", Art.Tile("medieval-hands", "MedievalHands", 64));
            return;
        }

        _pageList.ItemsSource = _manifest.Pages.Select(p => p.Label).ToList();
        _status.Text = $"{_manifest.Pages.Count:N0} pages";
        ShowTerms();

        _pageList.SelectedIndex = FirstLeaf(_manifest.Pages);
        _pageList.ScrollIntoView(_pageList.SelectedIndex);
    }

    /// <summary>
    /// Where to open the book.
    ///
    /// Not at the first image, which is a binding, a colour chart or a
    /// flyleaf in every one of these libraries - Gallica leads with four
    /// "page de garde", the Vatican with "piatto.anteriore", St Gall with
    /// "Front cover". The first leaf the library numbers is where the
    /// manuscript starts, so that is where this opens: "1r" at Gallica and
    /// the Vatican, "1" at e-codices.
    ///
    /// Falls back to the first image when nothing is numbered that way, which
    /// is better than an empty frame.
    /// </summary>
    internal static int FirstLeaf(IReadOnlyList<IiifPage> pages)
    {
        for (var i = 0; i < pages.Count; i++)
        {
            var label = pages[i].Label.Trim();
            if (label.Length == 0) continue;

            // "1r", "12v", "7" - a number, optionally with a side.
            var digits = label.TrimEnd('r', 'v', 'R', 'V');
            if (digits.Length > 0 && digits.All(char.IsDigit)) return i;
        }

        return 0;
    }

    /// <summary>
    /// Credits the library and links its own statement of conditions.
    ///
    /// Linked rather than paraphrased. These terms differ by library - the
    /// four behind CATMuS run from CC0 to "Images Copyright Biblioteca
    /// Apostolica Vaticana" - and a summary of somebody else's licence is how
    /// a summary ends up being the thing people rely on.
    /// </summary>
    private void ShowTerms()
    {
        if (_manifest == null) return;

        var credit = PlainText(_manifest.Attribution);
        if (credit.Length == 0) credit = "the holding library";

        _terms.Text = $"{_shelfmark} - images from {credit}, loaded from their server as you look at them and " +
                      "not saved to this computer.";

        _links.Children.Clear();
        AddLink("Conditions of use.", _manifest.LicenceUrl);
        AddLink("The library's own page.", _manifest.RelatedUrl);
    }

    private void AddLink(string label, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var icon = Glyphs.Icon("OpenExternal", 12);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var link = new Button
        {
            Classes = { "link" },
            Tag = label,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, icon }
            }
        };
        ToolTip.SetTip(link, url);

        // If macOS cannot open it there is nothing more useful to do here -
        // the same as every other link in this application.
        link.Click += (_, _) => MacShell.Open(url!);
        _links.Children.Add(link);
    }

    /// <summary>
    /// A manifest's attribution as words. IIIF lets a library put a little
    /// HTML in it - e-codices wraps its credit in a span - and the credit is
    /// owed in the library's own words, not in its markup.
    /// </summary>
    internal static string PlainText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private void Step(int by)
    {
        if (_manifest == null || _manifest.Pages.Count == 0) return;

        var index = Math.Clamp(_pageList.SelectedIndex + by, 0, _manifest.Pages.Count - 1);
        if (index == _pageList.SelectedIndex) return;

        _pageList.SelectedIndex = index;
        _pageList.ScrollIntoView(index);
    }

    private void OnZoomChanged()
    {
        _settingZoom = true;
        _zoom.SelectedIndex = _page.IsCustomZoom ? -1 : _page.Zoom switch
        {
            ManuscriptImageZoom.FitAll => 0,
            ManuscriptImageZoom.FitWidth => 1,
            ManuscriptImageZoom.Half => 2,
            ManuscriptImageZoom.Actual => 3,
            ManuscriptImageZoom.Double => 4,
            _ => -1
        };
        _settingZoom = false;

        _zoomPercent.Text = _page.Bitmap == null ? string.Empty : $"{_page.Scale * 100:0}%";

        if (_page.IsCustomZoom && _page.Bitmap != null)
        {
            _sharpen.Stop();
            _sharpen.Start();
        }
    }

    /// <summary>
    /// How many pixels wide to ask the library for.
    ///
    /// A leaf is around 2,900 by 4,100 at full resolution, which is several
    /// megabytes each. Asking for roughly what will be shown keeps a page turn
    /// to a fraction of that - but the magnifying zooms have to ask for more,
    /// or 200% is an enlargement of a thumbnail and shows the compression
    /// rather than the hand. A pinch asks for the next size up once it has
    /// magnified past the pixels the leaf on screen has.
    /// </summary>
    private int RequestWidth()
    {
        if (_page.IsCustomZoom)
        {
            return Magnified() ? (_loadedRequest < 1600 ? 1600 : 2400) : Math.Max(1200, _loadedRequest);
        }

        return _page.Zoom switch
        {
            ManuscriptImageZoom.Double => 2400,
            ManuscriptImageZoom.Actual => 1600,
            _ => 1200
        };
    }

    /// <summary>Whether the leaf is drawn larger, in the display's own pixels, than the image fetched for it.</summary>
    private bool Magnified()
    {
        if (_page.Bitmap is not { } bitmap) return false;
        var device = _page.DisplayedSize.Width * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        return device > bitmap.PixelSize.Width * 1.15;
    }

    /// <summary>
    /// Whether the leaf on screen has to be fetched again for the zoom now
    /// chosen. A fixed zoom is of the image fetched for it, as on Windows, so
    /// it needs exactly its own size; a fitting zoom is content with anything
    /// at least as large as it asks for; a pinch asks only to go larger.
    /// </summary>
    private bool NeedsFetch()
    {
        if (_manifest == null || _pageList.SelectedIndex < 0 || _loadedIndex != _pageList.SelectedIndex) return _manifest != null;

        var wanted = RequestWidth();
        if (_page.IsCustomZoom) return wanted > _loadedRequest && _loadedRequest < 2400;

        return _page.Zoom is ManuscriptImageZoom.Half or ManuscriptImageZoom.Actual or ManuscriptImageZoom.Double
            ? wanted != _loadedRequest
            : wanted > _loadedRequest;
    }

    private async Task ShowSelectedPageAsync(bool keepView = false)
    {
        if (_manifest == null || _pageList.SelectedIndex < 0) return;
        if (_pageList.SelectedIndex >= _manifest.Pages.Count) return;

        var index = _pageList.SelectedIndex;
        var page = _manifest.Pages[index];

        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        // Sharpening keeps the leaf on screen until the better one arrives;
        // a new leaf clears it, so nothing is shown under the wrong label.
        keepView &= _loadedIndex == index && _page.Bitmap != null;
        var width = RequestWidth();

        if (!keepView) _page.Show(null, $"Fetching {page.Label}...");
        _status.Text = $"{index + 1} of {_manifest.Pages.Count:N0} - {page.Label}" + (keepView ? " - fetching a sharper image..." : string.Empty);

        try
        {
            var bitmap = await Task.Run(async () =>
            {
                var bytes = await _http.GetByteArrayAsync(page.ImageUrl(width), token);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }, token);

            if (token.IsCancellationRequested || _closed)
            {
                bitmap.Dispose();
                return;
            }

            _loadedRequest = width;
            _loadedIndex = index;
            _status.Text = $"{index + 1} of {_manifest.Pages.Count:N0} - {page.Label}";

            if (keepView) _page.ReplaceKeepingView(bitmap);
            else _page.Show(bitmap, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _closed)
        {
            // Another leaf was chosen, or the window closed. Whichever request
            // is still running owns the panel now. Only this token's own
            // cancellation, though: the client's timeout is an
            // OperationCanceledException too, and on Windows it was swallowed
            // here as though the reader had moved on, leaving "Fetching..."
            // on screen for good.
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _closed) return;

            if (keepView) _status.Text = $"{index + 1} of {_manifest.Pages.Count:N0} - {page.Label}: {Explain(ex)}";
            else _page.Show(null, $"{page.Label}: {Explain(ex)}");
        }
    }

    /// <summary>
    /// What went wrong, in words a reader can act on.
    ///
    /// <b>429 is the one worth naming.</b> Gallica rate-limits, and it does so
    /// exactly when someone is doing the thing this window is for - turning
    /// pages quickly. A raw "Response status code does not indicate success:
    /// 429 (Too Many Requests)" reads like a fault in this application; it is
    /// the library asking to be left alone for a minute, and saying so is the
    /// difference between waiting and giving up.
    /// </summary>
    internal static string Explain(Exception exception)
    {
        var error = exception.GetBaseException();

        if (error is HttpRequestException http)
        {
            if (http.StatusCode is { } status && (int)status >= 500)
            {
                return "the library's own server is having trouble just now - it is worth trying again shortly.";
            }

            return http.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    "the library is asking for fewer requests just now - wait a minute and try again.",
                HttpStatusCode.NotFound =>
                    "the library no longer has this at the address it published.",
                HttpStatusCode.Forbidden =>
                    "the library is not serving this image to us.",
                _ => $"the library could not be reached ({error.Message})"
            };
        }

        // The client's timeout arrives as a TaskCanceledException wrapping a
        // TimeoutException, and the base exception is the inner one.
        return error is TaskCanceledException or TimeoutException
            ? "the library took too long to answer."
            : error.Message;
    }

    private static Button NavButton(string glyph, string tip, Action action)
    {
        var button = new Button { Classes = { "toolbar" }, Content = Glyphs.Icon(glyph, 14), Height = 28, MinWidth = 28 };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}
