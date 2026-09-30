using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Catmus;

namespace ClassicaCodex.UI;

/// <summary>
/// Medieval hands: 313 manuscripts from CATMuS-Medieval, each line as the
/// scribe wrote it beside a diplomatic transcription of it.
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
/// </summary>
public class PalaeographyForm : ScaledForm
{
    private readonly CatmusRepository _repository = new();

    private readonly ComboBox _languageFilter = new();
    private readonly ComboBox _centuryFilter = new();
    private readonly ComboBox _scriptFilter = new();
    private readonly CheckBox _downloadedOnly = new();
    private readonly TextBox _searchBox = new();
    private readonly CheckBox _exactCharacters = new();
    private readonly Button _searchButton = new();

    private readonly ListView _manuscriptList = new();
    private readonly ListView _lineList = new();
    private readonly ManuscriptImagePanel _specimen = new();
    private readonly Label _transcription = new();
    private readonly Label _licenceLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _getTextButton = new();
    private readonly Button _getImagesButton = new();
    private readonly Button _pagesButton = new();
    private readonly ComboBox _zoom = new();

    private List<CatmusManuscript> _shown = new();
    private List<CatmusLine> _lines = new();
    private Dictionary<string, CatmusHolding> _held = new(StringComparer.Ordinal);

    private CancellationTokenSource? _work;

    public PalaeographyForm()
    {
        Text = "Medieval Hands";
        AppIcons.ApplyWindowIcon(this, "Palaeography");
        ClientSize = new Size(1040, 690);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 500);

        Controls.Add(BuildBody());
        Controls.Add(BuildFilterStrip());
        Controls.Add(BuildFooter());

        ReadingTheme.AttachTo(this, RepaintSpecimen);
        WindowShortcuts.CloseOnEscape(this);

        Load += async (_, _) => await ReloadAsync();
        Shown += (_, _) => FitToScreen();
        FormClosing += (_, _) => _work?.Cancel();
    }

    /// <summary>
    /// Keeps the window inside the display it opens on.
    ///
    /// The design size is measured at 100%, and AutoScaleMode multiplies it -
    /// so 690 design pixels of height become 863 at 125% and 1035 at 150%.
    /// On a 1366x768 laptop, which is the machine this application is most
    /// often small-screened on, that opens a window taller than the desktop
    /// with its buttons below the bottom edge. Everything in here is docked,
    /// so shrinking costs nothing but a smaller pane.
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

    private Control BuildFilterStrip()
    {
        var strip = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 4) };

        foreach (var (combo, width, all, values) in new (ComboBox, int, string, IEnumerable<string>)[]
                 {
                     (_languageFilter, 130, "Any language", CatmusCatalogue.Languages),
                     (_centuryFilter, 110, "Any century",
                         CatmusCatalogue.Centuries.Select(c => c + OrdinalSuffix(c) + " century")),
                     (_scriptFilter, 180, "Any script", CatmusCatalogue.ScriptTypes)
                 })
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Width = width;
            combo.Items.Add(all);
            foreach (var value in values) combo.Items.Add(value);
            combo.SelectedIndex = 0;
            combo.SelectedIndexChanged += (_, _) => ShowManuscripts();
        }

        _downloadedOnly.Text = "Downloaded only";
        _downloadedOnly.AutoSize = true;
        _downloadedOnly.CheckedChanged += (_, _) => ShowManuscripts();

        _searchBox.Width = 190;
        _searchBox.PlaceholderText = "Find a word or a sign";
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RunSearchAsync();
        };

        _exactCharacters.Text = "Exactly as written";
        _exactCharacters.AutoSize = true;

        _searchButton.Text = "Search";
        _searchButton.Width = 76;
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        // Laid out right to left so the search half stays at the far end as
        // the window widens, and the filters stay at the near end.
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 580, WrapContents = false, AutoSize = false };
        foreach (Control c in new Control[] { _languageFilter, _centuryFilter, _scriptFilter, _downloadedOnly })
        {
            c.Margin = new Padding(0, 3, 8, 0);
            left.Controls.Add(c);
        }

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 420,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft
        };
        foreach (Control c in new Control[] { _searchButton, _exactCharacters, _searchBox })
        {
            c.Margin = new Padding(6, 3, 0, 0);
            right.Controls.Add(c);
        }

        strip.Controls.Add(right);
        strip.Controls.Add(left);
        return strip;
    }

    private Control BuildBody()
    {
        _manuscriptList.View = View.Details;
        _manuscriptList.FullRowSelect = true;
        _manuscriptList.MultiSelect = false;
        _manuscriptList.HideSelection = false;
        _manuscriptList.Dock = DockStyle.Fill;
        // Four columns, not five, and they add up to less than the pane they
        // sit in - a Details ListView does not shrink its columns to fit, it
        // grows a horizontal scrollbar and hides the last of them. The line
        // count went, because the panel underneath already gives it for
        // whichever manuscript is selected and the shelfmark needs the room
        // more: these run to "Auxerre, Archives départementales de l'Yonne".
        _manuscriptList.Columns.Add("Manuscript", 214);
        _manuscriptList.Columns.Add("Cent.", 46, HorizontalAlignment.Right);
        _manuscriptList.Columns.Add("Script", 102);
        _manuscriptList.Columns.Add("Photos", 66, HorizontalAlignment.Right);
        ReadingTheme.EnableThemedHeader(_manuscriptList);
        ScaleColumnsWhenShown(_manuscriptList);
        _manuscriptList.SelectedIndexChanged += async (_, _) => await ShowSelectedManuscriptAsync();

        _lineList.View = View.Details;
        _lineList.FullRowSelect = true;
        _lineList.MultiSelect = false;
        _lineList.HideSelection = false;
        _lineList.Dock = DockStyle.Fill;
        _lineList.Columns.Add("Transcription", 470);
        _lineList.Columns.Add("Zone", 100);
        ReadingTheme.EnableThemedHeader(_lineList);
        ScaleColumnsWhenShown(_lineList);
        _lineList.SelectedIndexChanged += async (_, _) => await ShowSelectedLineAsync();

        _specimen.Dock = DockStyle.Fill;

        // The transcription sits under the photograph in a size meant to be
        // compared with it, not skimmed - this is the only place in the
        // application where the two are read against each other.
        _transcription.Dock = DockStyle.Bottom;
        _transcription.Height = 52;
        _transcription.Font = new Font(Font.FontFamily, 12.5F);
        _transcription.Padding = new Padding(8, 6, 8, 0);
        _transcription.AutoEllipsis = false;

        // A Label eats an ampersand as a mnemonic prefix and underlines the
        // letter after it. These are somebody else's transcriptions and this
        // is the one place in the application where what is on screen has to
        // be character for character what the file says.
        _transcription.UseMnemonic = false;

        var specimenPane = new Panel { Dock = DockStyle.Fill };
        specimenPane.Controls.Add(_specimen);
        specimenPane.Controls.Add(_transcription);
        specimenPane.Controls.Add(BuildSpecimenToolbar());

        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 220
        };
        rightSplit.Panel1.Controls.Add(_lineList);
        rightSplit.Panel2.Controls.Add(specimenPane);

        // Wide enough for the four columns above plus the scrollbar; the
        // reader can drag it either way afterwards.
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 450 };
        split.Panel1.Controls.Add(_manuscriptList);
        split.Panel2.Controls.Add(rightSplit);

        // SplitterDistance assigned again once the handle exists. Before that
        // the container is at its default width and WinForms silently clamps
        // the value to fit it, so the panes open at the wrong proportions.
        split.HandleCreated += (_, _) => TrySetSplitter(split, 450);
        rightSplit.HandleCreated += (_, _) => TrySetSplitter(rightSplit, 220);

        return split;
    }

    /// <summary>
    /// Scales a Details-view list's column widths once its handle exists.
    ///
    /// AutoScaleMode scales a control's bounds and its font; it does not
    /// touch ListView column widths, which are set in device pixels through
    /// the native header. So at 150% every column here stays at its 100%
    /// width while the text inside it is half again as wide, and the
    /// shelfmarks are cut off mid-word.
    /// </summary>
    private static void ScaleColumnsWhenShown(ListView list) =>
        list.HandleCreated += (_, _) =>
        {
            foreach (ColumnHeader column in list.Columns)
            {
                column.Width = DpiScaling.Scale(list, column.Width);
            }
        };

    private static void TrySetSplitter(SplitContainer container, int designPixels)
    {
        try
        {
            container.SplitterDistance = DpiScaling.Scale(container, designPixels);
        }
        catch (InvalidOperationException)
        {
            // Thrown when the window is too small for the distance asked for.
            // The default is then as good an answer as any.
        }
    }

    private Control BuildSpecimenToolbar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 3, 8, 3) };

        _zoom.DropDownStyle = ComboBoxStyle.DropDownList;
        _zoom.Width = 120;
        _zoom.Items.AddRange(new object[] { "Fit the height", "Fit the width", "50%", "100%", "200%" });
        _zoom.SelectedIndex = 0;
        _zoom.SelectedIndexChanged += (_, _) =>
        {
            _specimen.Zoom = _zoom.SelectedIndex switch
            {
                0 => ManuscriptImageZoom.FitHeight,
                1 => ManuscriptImageZoom.FitWidth,
                2 => ManuscriptImageZoom.Half,
                3 => ManuscriptImageZoom.Actual,
                _ => ManuscriptImageZoom.Double
            };
        };

        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(10, 5, 0, 0);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        flow.Controls.Add(_zoom);
        flow.Controls.Add(_statusLabel);
        bar.Controls.Add(flow);
        return bar;
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 78, Padding = new Padding(8, 6, 8, 8) };

        _licenceLabel.Dock = DockStyle.Fill;
        _licenceLabel.Text = Introduction();
        _licenceLabel.UseMnemonic = false;

        _getTextButton.Text = "Download transcriptions";
        _getTextButton.Width = 168;
        _getTextButton.Height = 28;
        _getTextButton.Click += async (_, _) => await DownloadTextAsync();

        // The size is not on the button. It changes with the selection, and a
        // button whose caption grows by six characters either overflows its
        // own width or has to be sized for the widest manuscript in the
        // catalogue - which is "1.2 GB" and leaves the button half empty for
        // every other one. The panel above gives the size, and the
        // confirmation states it again before anything starts.
        _getImagesButton.Text = "Download photographs";
        _getImagesButton.Width = 160;
        _getImagesButton.Height = 28;
        _getImagesButton.Click += async (_, _) => await DownloadImagesAsync();

        // The page a line was cut out of, from the library that holds the
        // book - the decorated initials, the rubrics, the marginalia, the
        // illumination. Nothing is downloaded: see ManuscriptPagesForm.
        _pagesButton.Text = "See the pages";
        _pagesButton.Width = 110;
        _pagesButton.Height = 28;
        _pagesButton.Click += (_, _) => ShowPages();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 470,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(_pagesButton);
        buttons.Controls.Add(_getImagesButton);
        buttons.Controls.Add(_getTextButton);

        footer.Controls.Add(_licenceLabel);
        footer.Controls.Add(buttons);
        return footer;
    }

    private static string Introduction() =>
        $"{CatmusCatalogue.Manuscripts.Count} manuscripts, {CatmusCatalogue.TotalLines:N0} lines, " +
        "7th to 16th century, from CATMuS-Medieval. Transcriptions are a small download; " +
        "the photographs are the rest of the 24.7 GB, so they come one manuscript at a time.";

    private async Task ReloadAsync()
    {
        var holdings = await _repository.GetHoldingsAsync();
        _held = holdings.ToDictionary(h => h.Shelfmark, StringComparer.Ordinal);
        ShowManuscripts();
    }

    private void ShowManuscripts()
    {
        // Kept across the rebuild. A download finishes by reloading the
        // library, and without this the list would jump back to its first
        // manuscript - so fetching a gigabyte of photographs for the two
        // hundredth one would end by showing the first one instead.
        var wasSelected = SelectedManuscript?.Shelfmark;

        var language = _languageFilter.SelectedIndex > 0 ? (string)_languageFilter.SelectedItem! : null;
        var script = _scriptFilter.SelectedIndex > 0 ? (string)_scriptFilter.SelectedItem! : null;
        var century = _centuryFilter.SelectedIndex > 0
            ? CatmusCatalogue.Centuries[_centuryFilter.SelectedIndex - 1]
            : (int?)null;

        _shown = CatmusCatalogue.Manuscripts
            .Where(m => language == null || m.Language.Contains(language, StringComparison.Ordinal))
            .Where(m => script == null || m.ScriptType.Contains(script, StringComparison.Ordinal))
            .Where(m => century == null || m.Century == century)
            .Where(m => !_downloadedOnly.Checked || _held.ContainsKey(m.Shelfmark))
            .ToList();

        _manuscriptList.BeginUpdate();
        _manuscriptList.Items.Clear();

        foreach (var manuscript in _shown)
        {
            _held.TryGetValue(manuscript.Shelfmark, out var holding);

            // One sub-item per column after the first, in column order. A
            // spare one does not overflow - it silently lands under the next
            // heading and shifts every value after it left, which is how the
            // photograph sizes first appeared as line counts.
            var item = new ListViewItem(manuscript.Shelfmark);
            item.SubItems.Add(manuscript.Century?.ToString() ?? "");
            item.SubItems.Add(manuscript.ScriptType);
            item.SubItems.Add(holding?.HasImages == true ? "held" : FormatBytes(manuscript.Bytes));

            // A manuscript whose transcriptions are not downloaded is shown
            // in the muted colour rather than hidden: the catalogue is the
            // point of this list, and greying is the difference between "you
            // do not have this yet" and "this does not exist".
            if (holding == null) item.ForeColor = ReadingTheme.MutedText;

            _manuscriptList.Items.Add(item);
        }

        _manuscriptList.EndUpdate();
        _statusLabel.Text = $"{_shown.Count} of {CatmusCatalogue.Manuscripts.Count} manuscripts";

        if (_manuscriptList.Items.Count == 0)
        {
            UpdateButtons();
            return;
        }

        var index = wasSelected == null
            ? 0
            : Math.Max(0, _shown.FindIndex(m => string.Equals(m.Shelfmark, wasSelected, StringComparison.Ordinal)));

        _manuscriptList.Items[index].Selected = true;
        _manuscriptList.Items[index].EnsureVisible();
    }

    /// <summary>
    /// The selected manuscript, bounds-checked against the list it indexes.
    ///
    /// The index and the list are two objects that have to agree, and
    /// rebuilding the list raises a selection change while they briefly do
    /// not - so this asks rather than assumes.
    /// </summary>
    private CatmusManuscript? SelectedManuscript
    {
        get
        {
            if (_manuscriptList.SelectedIndices.Count == 0) return null;
            var index = _manuscriptList.SelectedIndices[0];
            return index >= 0 && index < _shown.Count ? _shown[index] : null;
        }
    }

    private async Task ShowSelectedManuscriptAsync()
    {
        var manuscript = SelectedManuscript;
        UpdateButtons();
        if (manuscript == null) return;

        _licenceLabel.Text = DescribeManuscript(manuscript);

        if (!_held.TryGetValue(manuscript.Shelfmark, out var holding))
        {
            _lines = new List<CatmusLine>();
            ShowLines("Not downloaded yet.");
            return;
        }

        _lines = await _repository.GetLinesAsync(holding.ManuscriptId, limit: 2000);
        ShowLines(null);
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
        return $"{facts}\r\n{state}  {pages}  {CatmusLicence.Describe(manuscript.Project)}";
    }

    private void ShowLines(string? emptyMessage)
    {
        _lineList.BeginUpdate();
        _lineList.Items.Clear();

        foreach (var line in _lines)
        {
            var item = new ListViewItem(line.Text) { Tag = line };
            item.SubItems.Add(line.Region ?? "");
            if (!line.HasImage) item.ForeColor = ReadingTheme.MutedText;
            _lineList.Items.Add(item);
        }

        _lineList.EndUpdate();

        if (_lineList.Items.Count > 0)
        {
            _lineList.Items[0].Selected = true;
        }
        else
        {
            _specimen.Show(null, emptyMessage ?? "No lines.");
            _transcription.Text = "";
        }
    }

    private async Task ShowSelectedLineAsync()
    {
        if (_lineList.SelectedItems.Count == 0) return;
        if (_lineList.SelectedItems[0].Tag is not CatmusLine line) return;

        _transcription.Text = line.Text;

        if (!line.HasImage)
        {
            _specimen.Show(null,
                "The photograph of this line has not been downloaded. " +
                "Use Download photographs to fetch this manuscript's.");
            return;
        }

        var bytes = await CatmusImageDownloadService.ReadImageAsync(DataFolderSettings.Root, line);
        if (bytes == null)
        {
            _specimen.Show(null, "The photograph could not be read from the download folder.");
            return;
        }

        try
        {
            // Copied into a MemoryStream the Image keeps: Image.FromStream
            // reads lazily, so a stream disposed here would leave an image
            // that throws on the next paint.
            _specimen.Show(Image.FromStream(new MemoryStream(bytes)), null);
        }
        catch (ArgumentException)
        {
            _specimen.Show(null, "The photograph is not an image this computer can read.");
        }
    }

    private void RepaintSpecimen() => _specimen.Invalidate();

    private void UpdateButtons()
    {
        var manuscript = SelectedManuscript;
        var busy = _work != null;

        _getTextButton.Enabled = manuscript != null && !busy && !_held.ContainsKey(manuscript.Shelfmark);
        _getImagesButton.Enabled = manuscript != null && !busy &&
                                   _held.TryGetValue(manuscript.Shelfmark, out var holding) && !holding.HasImages;

        // Independent of whether anything has been downloaded: the pages come
        // from the library, not from this library. It needs nothing but a
        // connection.
        _pagesButton.Enabled = manuscript != null && CatmusManifests.Has(manuscript.Shelfmark);
    }

    private void ShowPages()
    {
        var manuscript = SelectedManuscript;
        if (manuscript == null) return;

        var manifest = CatmusManifests.For(manuscript.Shelfmark);
        if (manifest == null) return;

        using var pages = new ManuscriptPagesForm(manuscript.Shelfmark, manifest);
        pages.ShowDialog(this);
    }

    private async Task DownloadTextAsync()
    {
        var manuscript = SelectedManuscript;
        if (manuscript == null) return;

        await RunAsync($"Reading {manuscript.Shelfmark}", async (progress, token) =>
        {
            var service = new CatmusIngestService(_repository);
            await service.IngestAsync(manuscript.Shards, progress, token);
        });
    }

    private async Task DownloadImagesAsync()
    {
        var manuscript = SelectedManuscript;
        if (manuscript == null) return;

        // Asked before it starts rather than reported while it runs: a
        // gigabyte is a thing to agree to, and Ghent UL 1374 really is one.
        var answer = MessageBox.Show(this,
            $"Download the line photographs of {manuscript.Shelfmark}?\r\n\r\n" +
            $"{FormatBytes(manuscript.Bytes)} over {manuscript.Shards.Count} file(s), " +
            $"for {manuscript.Lines:N0} lines.\r\n\r\n{CatmusLicence.ImageTerms}",
            "Download photographs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

        if (answer != DialogResult.OK) return;

        await RunAsync($"Fetching photographs of {manuscript.Shelfmark}", async (progress, token) =>
        {
            var service = new CatmusImageDownloadService(_repository);
            await service.DownloadAsync(manuscript, DataFolderSettings.Root, progress, token);
        });
    }

    /// <summary>
    /// Runs one download with the buttons disabled, the status line live, and
    /// the library reloaded afterwards whatever happened - including a
    /// cancellation, which may still have finished some of the shards.
    /// </summary>
    private async Task RunAsync(string what, Func<IProgress<string>, CancellationToken, Task> work)
    {
        _work = new CancellationTokenSource();
        UpdateButtons();
        _statusLabel.Text = what + "...";

        // Guarded because the window can be closed mid-download: closing
        // cancels the token, but the continuation still runs, and by then the
        // controls it would write to may be gone. This is the same shape of
        // bug as a cancelled Guided Setup step reporting into a closed
        // wizard.
        var progress = new Progress<string>(message =>
        {
            if (!IsDisposed) _statusLabel.Text = message;
        });

        try
        {
            await work(progress, _work.Token);
            if (!IsDisposed) _statusLabel.Text = what + " - done.";
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) _statusLabel.Text = what + " - stopped.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _statusLabel.Text = "Could not finish: " + ex.Message;
        }
        finally
        {
            _work?.Dispose();
            _work = null;

            if (!IsDisposed)
            {
                // Reloaded even after a cancellation: a stopped run may still
                // have finished some of a manuscript's files, and those are
                // in the library whether or not the rest arrived.
                await ReloadAsync();
            }
        }
    }

    private async Task RunSearchAsync()
    {
        var term = _searchBox.Text.Trim();
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

        _lines = await _repository.SearchAsync(
            term, _exactCharacters.Checked, manuscriptId: null,
            language: language, scriptType: script, century: century, limit: 500);

        if (_lines.Count == 0 && !_exactCharacters.Checked)
        {
            // A search for a sign that is not a letter - the Tironian et, a
            // bare combining mark - folds away to nothing, so the folded
            // search can only ever return nothing and saying "no results"
            // would be misleading about why.
            var folded = term.Any(char.IsLetter);
            _statusLabel.Text = folded
                ? "Nothing found."
                : "That sign has no letters in it - tick \"Exactly as written\" to search for the sign itself.";
        }
        else
        {
            _statusLabel.Text = $"{_lines.Count} line(s) across the library" +
                                (_lines.Count == 500 ? " (first 500)" : "");
        }

        ShowLines("Nothing found.");
    }

    private static string OrdinalSuffix(int value) => value switch
    {
        11 or 12 or 13 => "th",
        _ when value % 10 == 1 => "st",
        _ when value % 10 == 2 => "nd",
        _ when value % 10 == 3 => "rd",
        _ => "th"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1073741824 => $"{bytes / 1073741824.0:F1} GB",
        >= 1048576 => $"{bytes / 1048576.0:F0} MB",
        _ => $"{bytes / 1024.0:F0} KB"
    };
}
