using ClassicaCodex.Ingestion;
using ClassicaCodex.Ingestion.Catmus;

namespace ClassicaCodex.UI;

/// <summary>
/// The leaves of a manuscript, from the library that holds it.
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
/// </summary>
public class ManuscriptPagesForm : ScaledForm
{
    private readonly string _shelfmark;
    private readonly string _manifestUrl;

    private readonly ListBox _pageList = new();
    private readonly ManuscriptImagePanel _page = new();
    private readonly ComboBox _zoom = new();
    private readonly Label _status = new();
    private readonly LinkLabel _terms = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private IiifManifest? _manifest;

    /// <summary>
    /// Cancels the leaf being fetched when another is chosen. Someone running
    /// down the page list with the arrow keys starts a request per keypress,
    /// and without this the one that happens to finish last wins - so the
    /// picture on screen ends up belonging to a leaf that is no longer
    /// selected.
    /// </summary>
    private CancellationTokenSource? _loading;

    public ManuscriptPagesForm(string shelfmark, string manifestUrl)
    {
        _shelfmark = shelfmark;
        _manifestUrl = manifestUrl;

        Text = $"Pages - {shelfmark}";
        AppIcons.ApplyWindowIcon(this, "Palaeography");
        ClientSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 420);

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);

        Controls.Add(BuildBody());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildFooter());

        ReadingTheme.AttachTo(this, () => _page.Invalidate());
        WindowShortcuts.CloseOnEscape(this);

        Load += async (_, _) => await LoadManifestAsync();
        Shown += (_, _) => FitToScreen();
        FormClosing += (_, _) => _loading?.Cancel();
    }

    /// <summary>
    /// Keeps the window inside the display it opens on - see the same method
    /// on PalaeographyForm, which explains why a design size measured at 100%
    /// does not fit a laptop at 150%.
    /// </summary>
    private void FitToScreen()
    {
        var working = Screen.FromControl(this).WorkingArea;
        if (Width <= working.Width && Height <= working.Height) return;

        Size = new Size(Math.Min(Width, working.Width), Math.Min(Height, working.Height));
        Location = new Point(
            working.Left + Math.Max(0, (working.Width - Width) / 2),
            working.Top + Math.Max(0, (working.Height - Height) / 2));
    }

    private Control BuildBody()
    {
        _pageList.Dock = DockStyle.Fill;
        _pageList.IntegralHeight = false;
        _pageList.SelectedIndexChanged += async (_, _) => await ShowSelectedPageAsync();

        _page.Dock = DockStyle.Fill;
        _page.Zoom = ManuscriptImageZoom.FitAll;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 150 };
        split.Panel1.Controls.Add(_pageList);
        split.Panel2.Controls.Add(_page);

        // Assigned again once the handle exists: before that the container is
        // at its default width and WinForms clamps the value to fit it.
        split.HandleCreated += (_, _) =>
        {
            try
            {
                split.SplitterDistance = DpiScaling.Scale(split, 150);
            }
            catch (InvalidOperationException)
            {
                // The window is too small for the distance asked for; the
                // default is as good an answer as any.
            }
        };

        return split;
    }

    private Control BuildToolbar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 4, 8, 4) };

        _zoom.DropDownStyle = ComboBoxStyle.DropDownList;
        _zoom.Width = 130;
        _zoom.Items.AddRange(new object[] { "Fit the page", "Fit the width", "50%", "100%", "200%" });
        _zoom.SelectedIndex = 0;
        _zoom.SelectedIndexChanged += async (_, _) =>
        {
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
            await ShowSelectedPageAsync();
        };

        _status.AutoSize = true;
        _status.Padding = new Padding(10, 6, 0, 0);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        flow.Controls.Add(_zoom);
        flow.Controls.Add(_status);
        bar.Controls.Add(flow);
        return bar;
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8, 6, 8, 8) };

        _terms.Dock = DockStyle.Fill;
        _terms.UseMnemonic = false;
        _terms.Text = "Loading...";
        _terms.LinkClicked += (_, e) =>
        {
            if (e.Link?.LinkData is not string url) return;

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // If the shell cannot open it there is nothing more useful to
                // do here - the same as every other link in this application.
            }
        };

        footer.Controls.Add(_terms);
        return footer;
    }

    private async Task LoadManifestAsync()
    {
        _status.Text = "Asking the library...";

        try
        {
            _manifest = await IiifManifest.LoadAsync(_http, _manifestUrl);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _status.Text = "The library could not be reached.";
            _terms.Text = $"{_shelfmark}: {Explain(ex)}";
            return;
        }

        if (_manifest.Pages.Count == 0)
        {
            _status.Text = "No pages.";
            _terms.Text = $"{_shelfmark}: the library published a record for this manuscript but no page images " +
                          "this application can read.";
            return;
        }

        _pageList.BeginUpdate();
        _pageList.Items.Clear();
        foreach (var page in _manifest.Pages) _pageList.Items.Add(page.Label);
        _pageList.EndUpdate();

        _status.Text = $"{_manifest.Pages.Count:N0} pages";
        ShowTerms();

        _pageList.SelectedIndex = FirstLeaf(_manifest.Pages);
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
    private static int FirstLeaf(IReadOnlyList<IiifPage> pages)
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
    ///
    /// The whole string is assembled before it is assigned, because setting
    /// LinkLabel.Text rebuilds the Links collection with one link spanning
    /// everything: appending to the text after adding a link silently
    /// reinstates that default, and the next Add overlaps it and throws.
    /// </summary>
    private void ShowTerms()
    {
        if (_manifest == null) return;

        var credit = string.IsNullOrWhiteSpace(_manifest.Attribution)
            ? "the holding library"
            : _manifest.Attribution!.Trim();

        var text = new System.Text.StringBuilder(
            $"{_shelfmark} - images from {credit}, loaded from their server as you look at them and " +
            "not saved to this computer.");

        var links = new List<(int Start, int Length, string Url)>();

        void Append(string label, string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            text.Append("  ");
            links.Add((text.Length, label.Length, url!));
            text.Append(label);
        }

        Append("Conditions of use.", _manifest.LicenceUrl);
        Append("The library's own page.", _manifest.RelatedUrl);

        _terms.Text = text.ToString();
        _terms.Links.Clear();

        foreach (var (start, length, url) in links) _terms.Links.Add(start, length, url);
    }

    private async Task ShowSelectedPageAsync()
    {
        if (_manifest == null || _pageList.SelectedIndex < 0) return;
        if (_pageList.SelectedIndex >= _manifest.Pages.Count) return;

        var page = _manifest.Pages[_pageList.SelectedIndex];

        // Cancelled but not disposed. The request being cancelled is still
        // inside an await and will look at its own token on the way out;
        // disposing the source underneath it is asking for trouble for the
        // sake of an object that holds no timer and no registrations. The one
        // still standing is disposed with the form.
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        _page.Show(null, $"Fetching {page.Label}...");
        _status.Text = $"{_pageList.SelectedIndex + 1} of {_manifest.Pages.Count:N0} - {page.Label}";

        try
        {
            var bytes = await _http.GetByteArrayAsync(page.ImageUrl(RequestWidth()), token);
            if (token.IsCancellationRequested || IsDisposed) return;

            // Copied into a MemoryStream the Image keeps: Image.FromStream
            // reads lazily, so a stream disposed here would leave an image
            // that throws on the next paint.
            _page.Show(Image.FromStream(new MemoryStream(bytes)), null);
        }
        catch (OperationCanceledException)
        {
            // Another leaf was chosen. Whichever request is still running owns
            // the panel now.
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                _page.Show(null, $"{page.Label}: {Explain(ex)}");
            }
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
    private static string Explain(Exception exception)
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
                System.Net.HttpStatusCode.TooManyRequests =>
                    "the library is asking for fewer requests just now - wait a minute and try again.",
                System.Net.HttpStatusCode.NotFound =>
                    "the library no longer has this at the address it published.",
                System.Net.HttpStatusCode.Forbidden =>
                    "the library is not serving this image to us.",
                _ => $"the library could not be reached ({error.Message})"
            };
        }

        return error is TaskCanceledException
            ? "the library took too long to answer."
            : error.Message;
    }

    /// <summary>
    /// How many pixels wide to ask the library for.
    ///
    /// A leaf is around 2,900 by 4,100 at full resolution, which is several
    /// megabytes each. Asking for roughly what will be shown keeps a page turn
    /// to a fraction of that - but the magnifying zooms have to ask for more,
    /// or 200% is an enlargement of a thumbnail and shows the compression
    /// rather than the hand.
    /// </summary>
    private int RequestWidth() => _page.Zoom switch
    {
        ManuscriptImageZoom.Double => 2400,
        ManuscriptImageZoom.Actual => 1600,
        _ => 1200
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Nulled as it goes, because Dispose has to be safe to call
            // twice and a disposed CancellationTokenSource throws on Cancel.
            // Close() on a non-modal form disposes it, and a caller holding
            // it in a using disposes it again - which threw here.
            var loading = _loading;
            _loading = null;

            if (loading != null)
            {
                loading.Cancel();
                loading.Dispose();
            }

            _http.Dispose();
        }

        base.Dispose(disposing);
    }
}
