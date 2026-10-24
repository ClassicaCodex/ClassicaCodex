using System.Globalization;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Catmus;
using ClassicaCodex.Mac.Features.MedievalHands;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Medieval Hands and the pages window, against a private copy of the sample
/// library. The sample has no CATMuS in it, so the tests that need lines put
/// a few in the way the ingest does - through CatmusRepository, with real
/// catalogue shelfmarks - and photograph them: each line drawn into a small
/// PNG and packed the way the photograph download packs them. Nothing here
/// touches the network; the pages window is given a stand-in library.
/// </summary>
public class MedievalHandsTests
{
    private const string Latin = "Paris, BnF, lat. 12270";
    private const string French = "Paris, BnF, Arsenal, 3516";
    private const string Castilian = "Valladolid, Biblioteca de Santa Cruz, ms. 251";
    private const string Mixed = "Paris, BnF, esp. 36";

    /// <summary>
    /// Diplomatic transcriptions, abbreviations as written: the marks over
    /// letters, ꝯ ꝑ ꝓ, the long s, the Tironian et.
    /// </summary>
    private static readonly (string Shelfmark, bool Photographed, (string Text, string Zone)[] Lines)[] Specimens =
    {
        (Latin, true, new[]
        {
            ("In nomine dñi nr̃i ihu xp̃i ego ꝯcessisse ꝑpetuo", "MainZone"),
            ("ſcõ martino ⁊ monachis eius in loco qui dicitur", "MainZone"),
            ("uniu̾sitati fideliũ notũ eẽ uolum̃ quod ego", "MainZone"),
            ("dñicalis terre ꝓ remedio anime mee ⁊ nr̃e", "MainZone"),
            ("ut hoc donum firmum ⁊ ſtabile ſtet", "MainZone"),
            ("Incipit liber ſcintillarũ", "MarginTextZone")
        }),
        (French, true, new[]
        {
            ("Ci comence li romanz de la roſe", "MainZone"),
            ("ou lart damors eſt tote encloſe", "MainZone"),
            ("nr̃e ſeignor ⁊ ſa douce mere", "MainZone")
        }),
        (Castilian, false, new[]
        {
            ("q̃ los omnes ſabios dixieron", "MainZone"),
            ("⁊ por ende dize el filoſofo", "MainZone")
        })
    };

    [AvaloniaFact]
    public async Task Before_any_of_CATMuS_is_downloaded_the_whole_catalogue_is_listed_greyed_with_the_way_in()
    {
        await using var session = await Session.StartAsync(seed: false);
        if (session == null) return;

        MedievalHandsFeature.Open(session.Main);
        await Screens.Settle(6);
        var hands = session.Main.OwnedWindows.OfType<MedievalHandsWindow>().Single();
        await hands.WhenIdleAsync();
        await Screens.Settle(4);

        // The catalogue is embedded, so every manuscript is there before a byte
        // has been downloaded - and the window says how to get them.
        Assert.Equal(CatmusCatalogue.Manuscripts.Count, hands.ShownManuscripts.Count);
        Assert.Equal(313, hands.ShownManuscripts.Count);
        Assert.All(hands.ShownManuscripts, r => Assert.False(r.Held));
        Assert.Equal("313 of 313 manuscripts", hands.StatusText);
        Assert.True(hands.NotInstalledNoticeShown);

        // The first manuscript is chosen, described, and offered for download.
        var first = hands.ShownManuscripts[0].Manuscript;
        Assert.Same(hands.ShownManuscripts[0], hands.ManuscriptList.SelectedItem);
        Assert.StartsWith(first.Shelfmark + " - ", hands.DescriptionText);
        Assert.Contains("Not downloaded.", hands.DescriptionText);
        Assert.Contains("From CATMuS-Medieval (Clerice, Pinche et al.). Transcriptions: CC-BY 4.0", hands.DescriptionText);
        Assert.Equal("Not downloaded yet.", hands.EmptyLinesMessage);
        Assert.Empty(hands.Lines);
        Assert.True(hands.GetTextButton.IsEnabled);
        Assert.False(hands.GetImagesButton.IsEnabled);
        Assert.Equal(CatmusManifests.Has(first.Shelfmark), hands.PagesButton.IsEnabled);

        Screens.Save(hands, "medieval-01-catalogue-not-installed-light");

        // One window: opening it again brings this one forward.
        MedievalHandsFeature.Open(session.Main);
        await Screens.Settle(2);
        Assert.Single(session.Main.OwnedWindows.OfType<MedievalHandsWindow>());

        // The filters come from the catalogue and narrow it.
        hands.LanguageFilter.SelectedItem = "Castilian";
        hands.CenturyFilter.SelectedItem = "14th century";
        await hands.WhenIdleAsync();
        Assert.NotEmpty(hands.ShownManuscripts);
        Assert.All(hands.ShownManuscripts, r =>
        {
            Assert.Contains("Castilian", r.Manuscript.Language);
            Assert.Equal(14, r.Manuscript.Century);
        });
        Assert.Equal($"{hands.ShownManuscripts.Count} of 313 manuscripts", hands.StatusText);

        // Nothing held, so "Downloaded only" empties the list - and says so.
        hands.DownloadedOnly.IsChecked = true;
        await hands.WhenIdleAsync();
        Assert.Empty(hands.ShownManuscripts);
        Assert.Equal("No manuscript in the catalogue matches these filters.", hands.EmptyLinesMessage);
        Assert.False(hands.GetTextButton.IsEnabled);

        hands.DownloadedOnly.IsChecked = false;
        hands.LanguageFilter.SelectedIndex = 0;
        hands.CenturyFilter.SelectedIndex = 0;
        await hands.WhenIdleAsync();

        // The share-alike, non-commercial manuscripts say so before anything
        // is downloaded, and the one that is half of each says both.
        hands.SelectManuscript(Castilian);
        await hands.WhenIdleAsync();
        Assert.Contains("CC-BY-NC-SA 4.0 - share alike, non-commercial use only.", hands.DescriptionText);
        Assert.Contains("Its pages are not published anywhere this can reach.", hands.DescriptionText);
        Assert.False(hands.PagesButton.IsEnabled);

        hands.SelectManuscript(Mixed);
        await hands.WhenIdleAsync();
        Assert.Contains("Transcriptions: mixed", hands.DescriptionText);
        Assert.Contains("CC-BY-NC-SA 4.0", hands.DescriptionText);
        Assert.Contains("CC-BY 4.0", hands.DescriptionText);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(hands, "medieval-02-catalogue-not-installed-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        hands.Close();
    }

    [AvaloniaFact]
    public async Task A_held_manuscript_shows_each_line_beside_its_photograph()
    {
        await using var session = await Session.StartAsync(seed: true);
        if (session == null) return;

        var hands = await session.OpenAsync();
        Assert.False(hands.NotInstalledNoticeShown);

        // Held manuscripts are not greyed, and "held" stands where the size was.
        hands.DownloadedOnly.IsChecked = true;
        await hands.WhenIdleAsync();
        Assert.Equal(new[] { French, Latin, Castilian }, hands.ShownManuscripts.Select(r => r.Manuscript.Shelfmark));
        Assert.All(hands.ShownManuscripts, r => Assert.True(r.Held));
        Assert.Equal("held", hands.ShownManuscripts.Single(r => r.Manuscript.Shelfmark == Latin).Photos);
        Assert.EndsWith("MB", hands.ShownManuscripts.Single(r => r.Manuscript.Shelfmark == Castilian).Photos);

        hands.SelectManuscript(Latin);
        await hands.WhenIdleAsync();

        Assert.Equal(6, hands.Lines.Count);
        Assert.Equal("LINES", hands.LinesTitle);
        Assert.Equal("6", hands.LineCountText);
        Assert.Contains("Transcriptions and photographs are on this computer.", hands.DescriptionText);
        Assert.Contains("Its pages can be seen.", hands.DescriptionText);
        Assert.Contains("Transcriptions: CC-BY 4.0 - free to reuse with attribution.", hands.DescriptionText);
        Assert.False(hands.GetTextButton.IsEnabled);
        Assert.False(hands.GetImagesButton.IsEnabled);
        Assert.True(hands.PagesButton.IsEnabled);

        // The first line chosen, its photograph on screen and its
        // transcription under it, character for character.
        Assert.Equal(0, hands.LineList.SelectedIndex);
        Assert.Equal("In nomine dñi nr̃i ihu xp̃i ego ꝯcessisse ꝑpetuo", hands.TranscriptionText);
        Assert.NotNull(hands.Specimen.Bitmap);
        Assert.Null(hands.Specimen.Message);

        // Fit the height: the line is as tall as the pane, and wider than it.
        Assert.Equal(0, hands.ZoomBox.SelectedIndex);
        Assert.Equal(ManuscriptImageZoom.FitHeight, hands.Specimen.Zoom);
        var bitmap = hands.Specimen.Bitmap!;
        Assert.InRange(hands.Specimen.Scale, 0.5, 6);
        Assert.Equal(bitmap.PixelSize.Width * hands.Specimen.Scale, hands.Specimen.DisplayedSize.Width, 0);

        await Screens.Settle(4);
        Screens.Save(hands, "medieval-03-line-beside-photograph-light");

        // Fit the width shows the whole line; 200% is twice its pixels.
        hands.ZoomBox.SelectedIndex = 1;
        await Screens.Settle(2);
        Assert.Equal(ManuscriptImageZoom.FitWidth, hands.Specimen.Zoom);
        Assert.True(hands.Specimen.DisplayedSize.Width <= hands.Specimen.Bounds.Width + 1);

        hands.ZoomBox.SelectedIndex = 4;
        await Screens.Settle(2);
        Assert.Equal(2.0, hands.Specimen.Scale);

        // ⌘- steps out of the presets, and the picker says so.
        Assert.True(hands.Specimen.TryHandleZoomKey(new KeyEventArgs { Key = Key.OemMinus, KeyModifiers = KeyModifiers.Meta }));
        await Screens.Settle(1);
        Assert.True(hands.Specimen.IsCustomZoom);
        Assert.Equal(1.6, hands.Specimen.Scale, 3);
        Assert.Equal(-1, hands.ZoomBox.SelectedIndex);

        // ⌘0 is the photograph's own size; ⌘9 fits again, and the picker
        // follows both.
        Assert.True(hands.Specimen.TryHandleZoomKey(new KeyEventArgs { Key = Key.D0, KeyModifiers = KeyModifiers.Meta }));
        await Screens.Settle(1);
        Assert.Equal(1.0, hands.Specimen.Scale);
        Assert.Equal(3, hands.ZoomBox.SelectedIndex);

        Assert.True(hands.Specimen.TryHandleZoomKey(new KeyEventArgs { Key = Key.D9, KeyModifiers = KeyModifiers.Meta }));
        await Screens.Settle(1);
        Assert.False(hands.Specimen.IsCustomZoom);
        Assert.Equal(ManuscriptImageZoom.FitHeight, hands.Specimen.Zoom);
        Assert.Equal(0, hands.ZoomBox.SelectedIndex);

        // Zooming about a point keeps that point of the photograph under it.
        var anchor = new Point(300, 60);
        var fitted = hands.Specimen.Scale;
        var pixel = new Point((hands.Specimen.Offset.X + anchor.X) / fitted, (hands.Specimen.Offset.Y + anchor.Y) / fitted);
        hands.Specimen.ZoomBy(2, anchor);
        await Screens.Settle(1);
        Assert.True(hands.Specimen.IsCustomZoom);
        Assert.Equal(fitted * 2, hands.Specimen.Scale, 6);
        Assert.Equal(pixel.X * hands.Specimen.Scale - anchor.X, hands.Specimen.Offset.X, 0);
        Assert.Equal(pixel.Y * hands.Specimen.Scale - anchor.Y, hands.Specimen.Offset.Y, 0);
        hands.ZoomBox.SelectedIndex = 0;

        // Another line, another photograph.
        hands.LineList.SelectedIndex = 3;
        await hands.WhenIdleAsync();
        Assert.Equal("dñicalis terre ꝓ remedio anime mee ⁊ nr̃e", hands.TranscriptionText);
        Assert.NotNull(hands.Specimen.Bitmap);
        Assert.NotSame(bitmap, hands.Specimen.Bitmap);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(hands, "medieval-04-line-beside-photograph-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        // The last line has no photograph: it is muted, and the panel says why.
        hands.LineList.SelectedIndex = 5;
        await hands.WhenIdleAsync();
        Assert.False(hands.Lines[5].HasImage);
        Assert.Null(hands.Specimen.Bitmap);
        Assert.StartsWith("The photograph of this line has not been downloaded.", hands.Specimen.Message);

        // Transcriptions without photographs: the photographs are offered.
        hands.SelectManuscript(Castilian);
        await hands.WhenIdleAsync();
        Assert.Equal(2, hands.Lines.Count);
        Assert.Contains("Transcriptions are on this computer; photographs are not.", hands.DescriptionText);
        Assert.True(hands.GetImagesButton.IsEnabled);
        Assert.False(hands.GetTextButton.IsEnabled);

        // A pack that has gone from the download folder is a line with no
        // picture, not a crash.
        hands.DataRoot = () => Path.Combine(session.DataRoot, "moved-away");
        hands.SelectManuscript(French);
        await hands.WhenIdleAsync();
        Assert.Equal("The photograph could not be read from the download folder.", hands.Specimen.Message);

        hands.Close();
    }

    [AvaloniaFact]
    public async Task Search_folds_the_marks_over_letters_or_matches_the_sign_exactly()
    {
        await using var session = await Session.StartAsync(seed: true);
        if (session == null) return;

        var hands = await session.OpenAsync();

        // Folded: "nre" in plain letters finds the lines the scribes wrote nr̃e.
        await hands.SearchAsync("nre", exactly: false);
        await hands.WhenIdleAsync();
        Assert.True(hands.ShowingSearchResults);
        Assert.Equal("LINES FOUND ACROSS THE LIBRARY", hands.LinesTitle);
        Assert.Equal(2, hands.Lines.Count);
        Assert.All(hands.Lines, l => Assert.Contains("nr̃e", l.Text));
        Assert.Equal(new[] { Latin, French }.OrderBy(s => s).ToList(), hands.Lines.Select(l => l.Shelfmark).OrderBy(s => s).ToList());
        Assert.StartsWith("2 line(s) across the library", hands.StatusText);

        // The long s folds to s, so "stet" finds ſtet.
        await hands.SearchAsync("stet", exactly: false);
        await hands.WhenIdleAsync();
        Assert.Single(hands.Lines);
        Assert.Contains("ſtet", hands.Lines[0].Text);

        // A bare sign folds away to nothing, and the window says why.
        await hands.SearchAsync("⁊", exactly: false);
        await hands.WhenIdleAsync();
        Assert.Empty(hands.Lines);
        Assert.Equal("That sign has no letters in it - tick \"Exactly as written\" to search for the sign itself.", hands.StatusText);
        Assert.Equal("Nothing found.", hands.EmptyLinesMessage);

        await Screens.Settle(4);
        Screens.Save(hands, "medieval-05-search-nothing-found");

        // Exactly as written finds every line where a scribe wrote it.
        await hands.SearchAsync("⁊", exactly: true);
        await hands.WhenIdleAsync();
        Assert.Equal(5, hands.Lines.Count);
        Assert.All(hands.Lines, l => Assert.Contains("⁊", l.Text));
        Assert.Contains(hands.Lines, l => l.Shelfmark == Castilian);

        await Screens.Settle(4);
        Screens.Save(hands, "medieval-06-search-tironian-et");

        // The filters apply to the search.
        hands.LanguageFilter.SelectedItem = "French";
        await hands.WhenIdleAsync();
        await hands.SearchAsync("⁊", exactly: true);
        await hands.WhenIdleAsync();
        Assert.Single(hands.Lines);
        Assert.Equal(French, hands.Lines[0].Shelfmark);
        hands.LanguageFilter.SelectedIndex = 0;
        await hands.WhenIdleAsync();

        // A sign a fold would keep - ꝯ is a letter - is found either way.
        await hands.SearchAsync("ꝯ", exactly: true);
        await hands.WhenIdleAsync();
        Assert.Single(hands.Lines);
        Assert.Contains("ꝯcessisse", hands.Lines[0].Text);

        // An empty search goes back to the manuscript's own lines.
        await hands.SearchAsync("   ", exactly: false);
        await hands.WhenIdleAsync();
        Assert.False(hands.ShowingSearchResults);
        Assert.Equal("LINES", hands.LinesTitle);

        hands.Close();
    }

    [AvaloniaFact]
    public async Task The_terms_beside_a_found_line_are_its_own_manuscripts_and_double_click_opens_it()
    {
        await using var session = await Session.StartAsync(seed: true);
        if (session == null) return;

        var hands = await session.OpenAsync();
        hands.SelectManuscript(Latin);
        await hands.WhenIdleAsync();
        Assert.Contains("CC-BY 4.0", hands.DescriptionText);

        await hands.SearchAsync("⁊", exactly: true);
        await hands.WhenIdleAsync();

        // A line from the Castilian manuscript, found while the Latin one is
        // selected in the list: the panel describes the Castilian one, with
        // its share-alike, non-commercial terms, and the buttons act on it.
        var index = hands.Lines.ToList().FindIndex(l => l.Shelfmark == Castilian);
        hands.LineList.SelectedIndex = index;
        await hands.WhenIdleAsync();
        Assert.StartsWith(Castilian + " - ", hands.DescriptionText);
        Assert.Contains("CC-BY-NC-SA 4.0 - share alike, non-commercial use only.", hands.DescriptionText);
        Assert.True(hands.GetImagesButton.IsEnabled);
        Assert.False(hands.PagesButton.IsEnabled);
        Assert.Equal(Latin, ((ManuscriptRow)hands.ManuscriptList.SelectedItem!).Manuscript.Shelfmark);

        // Opening a found line lists its manuscript with that line chosen.
        var line = hands.Lines[index];
        await hands.OpenResultsManuscriptAsync();
        await hands.WhenIdleAsync();
        Assert.False(hands.ShowingSearchResults);
        Assert.Equal(Castilian, ((ManuscriptRow)hands.ManuscriptList.SelectedItem!).Manuscript.Shelfmark);
        Assert.Equal(line.LineId, hands.Lines[hands.LineList.SelectedIndex].LineId);
        Assert.Equal(line.Text, hands.TranscriptionText);

        hands.Close();
    }

    [AvaloniaFact]
    public async Task Coming_back_to_the_window_shows_what_Setup_added_meanwhile()
    {
        await using var session = await Session.StartAsync(seed: false);
        if (session == null) return;

        var hands = await session.OpenAsync();
        hands.SelectManuscript(Latin);
        await hands.WhenIdleAsync();
        Assert.True(hands.NotInstalledNoticeShown);
        Assert.Equal("Not downloaded yet.", hands.EmptyLinesMessage);

        // Setup's step runs while the window is open - here, the same
        // repository calls - and the reader comes back to it.
        await SeedAsync(session.DataRoot);
        session.Main.Activate();
        await Screens.Settle(2);
        hands.Activate();
        await Screens.Settle(4);
        await hands.WhenIdleAsync();

        Assert.False(hands.NotInstalledNoticeShown);
        Assert.Equal(3, hands.ShownManuscripts.Count(r => r.Held));

        // Still on the manuscript it was on, now with its lines.
        Assert.Equal(Latin, ((ManuscriptRow)hands.ManuscriptList.SelectedItem!).Manuscript.Shelfmark);
        Assert.Equal(6, hands.Lines.Count);
        Assert.Null(hands.EmptyLinesMessage);

        hands.Close();
    }

    [AvaloniaFact]
    public async Task The_pages_come_from_the_library_open_at_the_first_numbered_leaf_and_carry_its_credit()
    {
        await using var session = await Session.StartAsync(seed: false);
        if (session == null) return;

        var library = new StandInLibrary();
        var manifest = CatmusManifests.For(Latin)!;
        var pages = new ManuscriptPagesWindow(Latin, manifest, library);
        pages.Show(session.Main);
        await Screens.Settle(4);
        await pages.WhenIdleAsync();
        await Screens.Settle(4);

        Assert.Equal($"Pages - {Latin}", pages.Title);

        // Not at the binding or the flyleaves: at 1r, the first leaf the
        // library numbers.
        Assert.Equal(5, pages.PageList.ItemCount);
        Assert.Equal(2, pages.PageList.SelectedIndex);
        Assert.Equal("3 of 5 - 1r", pages.StatusText);
        Assert.NotNull(pages.Page.Bitmap);
        Assert.Equal(ManuscriptImageZoom.FitAll, pages.Page.Zoom);

        // The library's credit in its own words, without its markup, and its
        // own statement of conditions linked rather than paraphrased.
        Assert.Equal(
            $"{Latin} - images from Bibliothèque nationale de France, loaded from their server as you look at them and not saved to this computer.",
            pages.TermsText);
        Assert.Equal(new[] { "Conditions of use.", "The library's own page." }, pages.Links.Select(l => l.Label));
        Assert.Equal(StandInLibrary.Conditions, pages.Links[0].Url);

        // One request for the manifest, one for the leaf, and nothing for the
        // leaves nobody looked at.
        Assert.Equal(manifest, library.Requests[0]);
        Assert.Equal(2, library.Requests.Count);
        Assert.Contains("/f3/full/", library.Requests[1]);
        Assert.Equal(1200, pages.LoadedRequest);

        Screens.Save(pages, "medieval-07-pages-light");

        // 200% asks for a bigger image rather than enlarging the small one.
        pages.ZoomBox.SelectedIndex = 4;
        await Screens.Settle(2);
        await pages.WhenIdleAsync();
        Assert.Equal(2400, pages.LoadedRequest);
        Assert.Equal(3, library.Requests.Count);

        pages.ZoomBox.SelectedIndex = 0;
        await Screens.Settle(2);
        await pages.WhenIdleAsync();

        // Fitting again needs nothing new: the bigger image fits as well.
        Assert.Equal(3, library.Requests.Count);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(pages, "medieval-08-pages-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        // A library asking to be left alone is named as that.
        pages.PageList.SelectedIndex = 3;
        await Screens.Settle(2);
        await pages.WhenIdleAsync();
        Assert.Null(pages.Page.Bitmap);
        Assert.Equal("1v: the library is asking for fewer requests just now - wait a minute and try again.", pages.Page.Message);

        pages.Close();
    }

    [AvaloniaFact]
    public async Task A_library_that_cannot_be_reached_says_so_in_the_pages_window()
    {
        await using var session = await Session.StartAsync(seed: false);
        if (session == null) return;

        var pages = new ManuscriptPagesWindow(Latin, "https://library.test/missing/manifest.json", new StandInLibrary());
        pages.Show(session.Main);
        await Screens.Settle(4);
        await pages.WhenIdleAsync();

        Assert.Equal("The library could not be reached.", pages.StatusText);
        Assert.Equal($"{Latin}: the library no longer has this at the address it published.", pages.TermsText);
        Assert.Empty(pages.Links);
        pages.Close();
    }

    [AvaloniaFact]
    public void The_first_leaf_is_the_first_one_numbered_and_an_attribution_loses_its_markup()
    {
        static IiifPage Leaf(string label) => new() { Label = label, ImageService = "https://library.test/x" };

        Assert.Equal(4, ManuscriptPagesWindow.FirstLeaf(new[] { Leaf("plat supérieur"), Leaf("page de garde recto"), Leaf("page de garde verso"), Leaf(""), Leaf("1r") }));
        Assert.Equal(1, ManuscriptPagesWindow.FirstLeaf(new[] { Leaf("Front cover"), Leaf("1"), Leaf("2") }));
        Assert.Equal(0, ManuscriptPagesWindow.FirstLeaf(new[] { Leaf("piatto.anteriore"), Leaf("guardia") }));

        Assert.Equal("e-codices - Virtual Manuscript Library of Switzerland",
            ManuscriptPagesWindow.PlainText("<span>e-codices - Virtual Manuscript Library of Switzerland</span>"));
        Assert.Equal("Bibliothèque nationale de France", ManuscriptPagesWindow.PlainText("Biblioth&#232;que nationale de France"));
        Assert.Equal(string.Empty, ManuscriptPagesWindow.PlainText(null));

        // The medieval faces first, in order, then the whole of the reading
        // face's list - so a Mac with none of them still draws in Palatino.
        var font = MedievalHandsWindow.TranscriptionFont().FamilyNames.ToList();
        Assert.Equal(new[] { "Junicode", "Andron Scriptor Web", "Andron Scriptor", "Palemonas MUFI", "Palemonas", "Noto Serif", "Palatino" }, font.Take(7));
        Assert.Contains("DejaVu Serif", font);

        Assert.Equal("the library took too long to answer.",
            ManuscriptPagesWindow.Explain(new TaskCanceledException("timeout", new TimeoutException("elapsed"))));
        Assert.Equal("the library's own server is having trouble just now - it is worth trying again shortly.",
            ManuscriptPagesWindow.Explain(new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway)));
    }

    // --- the library, the lines and their photographs ------------------------

    /// <summary>
    /// The main window over a private copy of the sample library, with the
    /// specimens above put into it when asked and their photographs in a
    /// download folder of its own.
    /// </summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly string _folder;

        private Session(string folder, MainWindow main)
        {
            _folder = folder;
            Main = main;
        }

        public MainWindow Main { get; }

        public string DataRoot => Path.Combine(_folder, "data");

        public static async Task<Session?> StartAsync(bool seed)
        {
            if (Screens.SampleLibrary is not { } library) return null;

            var folder = Path.Combine(Path.GetTempPath(), "classicacodex-mac-medieval-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, "library.db");
            File.Copy(library, copy);
            File.SetAttributes(copy, FileAttributes.Normal);

            DbConnectionFactory.Configure(copy, remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            var session = new Session(folder, new MainWindow { Width = 1360, Height = 860 });
            if (seed) await SeedAsync(session.DataRoot);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            session.Main.Show();
            await Screens.Settle(8);
            return session;
        }

        /// <summary>Opens the window on this session's download folder.</summary>
        public async Task<MedievalHandsWindow> OpenAsync()
        {
            var hands = new MedievalHandsWindow { DataRoot = () => DataRoot };
            hands.Show(Main);
            await Screens.Settle(6);
            await hands.WhenIdleAsync();
            await Screens.Settle(2);
            return hands;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var owned in Main.OwnedWindows.ToList()) owned.Close();
            Main.Close();
            await Screens.Settle(2);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Puts the specimens in the library as the two downloads would: the
    /// transcriptions shard by shard, then - for the photographed ones - a
    /// pack of PNGs in the download folder with each line's place in it, all
    /// but the last line of the Latin manuscript, so a line without a
    /// photograph is there too.
    /// </summary>
    private static async Task SeedAsync(string dataRoot)
    {
        var repository = new CatmusRepository();

        foreach (var (shelfmark, photographed, lines) in Specimens)
        {
            var shard = CatmusCatalogue.Find(shelfmark)!.Shards[0];
            var rows = lines
                .Select((line, i) => new CatmusLine { RowIndex = i, Text = line.Text, Region = line.Zone, LineType = "DefaultLine" })
                .ToList();

            await repository.ReplaceShardAsync(
                shelfmark, shard.Language, shard.Century, shard.ScriptType, shard.Genre, shard.Verse, shard.Project,
                shard.FileName, rows);

            if (!photographed) continue;

            var pack = CatmusImageDownloadService.PackFileName(shelfmark);
            var path = CatmusImageDownloadService.PackPath(dataRoot, pack);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var placements = new Dictionary<(string ShardFile, int RowIndex), (long Offset, int Length)>();
            await using (var stream = File.Create(path))
            {
                var photographedLines = shelfmark == Latin ? rows.Count - 1 : rows.Count;
                for (var i = 0; i < photographedLines; i++)
                {
                    var png = LinePhotograph(rows[i].Text, i);
                    placements[(shard.FileName, i)] = (stream.Position, png.Length);
                    await stream.WriteAsync(png);
                }
            }

            await repository.SetImagePackAsync(shelfmark, pack, placements);
        }
    }

    /// <summary>A line as a camera might have seen it: ink on vellum, a strip much wider than it is tall.</summary>
    private static byte[] LinePhotograph(string text, int seed)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(1500, 72), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var vellum = Color.FromRgb((byte)(228 - seed * 3), (byte)(214 - seed * 2), 182);
            context.FillRectangle(new SolidColorBrush(vellum), new Rect(0, 0, 1500, 72));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 120, 90, 60)), 1), new Point(0, 56), new Point(1500, 56));

            var ink = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Bitstream Charter, Caladea, serif", FontStyle.Italic), 40, new SolidColorBrush(Color.FromRgb(58, 38, 24)));
            context.DrawText(ink, new Point(24, 6));
        }

        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        return png.ToArray();
    }

    /// <summary>
    /// A holding library that answers from memory: a IIIF manifest of five
    /// canvases - a binding, two flyleaves, 1r and 1v - and a drawn leaf for
    /// every image request, at the size asked for, except 1v, which is
    /// rate-limited the way Gallica rate-limits. Any other address is a 404.
    /// </summary>
    private sealed class StandInLibrary : HttpMessageHandler
    {
        public const string Conditions = "https://gallica.bnf.fr/edit/und/conditions-dutilisation-des-contenus-de-gallica";

        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);

            if (url == CatmusManifests.For(Latin))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Manifest(), Encoding.UTF8, "application/json")
                });
            }

            if (url.StartsWith("https://library.test/iiif/", StringComparison.Ordinal))
            {
                if (url.Contains("/f4/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Leaf(SizeAsked(url)))
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string Manifest()
        {
            static string Canvas(string label, string id) =>
                "{\"@type\":\"sc:Canvas\",\"label\":\"" + label + "\",\"width\":2857,\"height\":4096," +
                "\"images\":[{\"resource\":{\"@id\":\"https://library.test/full/" + id + ".jpg\"," +
                "\"service\":{\"@id\":\"https://library.test/iiif/" + id + "/\"}}}]}";

            return $$"""
                {"@context":"http://iiif.io/api/presentation/2/context.json","@type":"sc:Manifest",
                 "label":"Latin 12270",
                 "attribution":"<span>Biblioth&#232;que nationale de France</span>",
                 "license":"{{Conditions}}",
                 "related":"https://gallica.bnf.fr/ark:/12148/btv1b9068316t",
                 "sequences":[{"canvases":[
                   {{Canvas("plat supérieur", "f1")}},
                   {{Canvas("page de garde recto", "f2")}},
                   {{Canvas("1r", "f3")}},
                   {{Canvas("1v", "f4")}},
                   {{Canvas("2r", "f5")}}
                 ]}]}
                """;
        }

        /// <summary>The number in the size segment of a IIIF image request - whichever side it constrains.</summary>
        private static int SizeAsked(string url)
        {
            var segment = url[(url.IndexOf("/full/", StringComparison.Ordinal) + 6)..];
            segment = segment[..segment.IndexOf('/')];
            var digits = new string(segment.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var size) ? size : 1200;
        }

        /// <summary>A page-shaped leaf: vellum, ruled, written in two columns with a red initial.</summary>
        private static byte[] Leaf(int longSide)
        {
            var height = Math.Clamp(longSide, 200, 2400);
            var width = (int)(height * 0.7);
            var scale = height / 1000.0;

            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(232, 219, 190)), new Rect(0, 0, width, height));
                var ink = new SolidColorBrush(Color.FromRgb(60, 42, 28));
                var rule = new Pen(new SolidColorBrush(Color.FromArgb(70, 150, 110, 80)), Math.Max(1, scale));

                for (var column = 0; column < 2; column++)
                {
                    var left = (90 + column * 300) * scale;
                    for (var row = 0; row < 30; row++)
                    {
                        var y = (120 + row * 26) * scale;
                        context.DrawLine(rule, new Point(left, y), new Point(left + 250 * scale, y));
                        var length = (row * 37 + column * 13) % 5 == 0 ? 160 : 240;
                        context.FillRectangle(ink, new Rect(left + (row < 3 && column == 0 ? 60 : 0) * scale, y - 12 * scale, (length - (row < 3 && column == 0 ? 60 : 0)) * scale, 7 * scale));
                    }
                }

                context.FillRectangle(new SolidColorBrush(Color.FromRgb(170, 40, 30)), new Rect(90 * scale, 96 * scale, 52 * scale, 64 * scale));
            }

            using var png = new MemoryStream();
            bitmap.Save(png, PngBitmapEncoderOptions.Default);
            return png.ToArray();
        }
    }
}
