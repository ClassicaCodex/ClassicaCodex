using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core.Reactions;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.About;
using ClassicaCodex.Mac.Features.Apparatus;
using ClassicaCodex.Mac.Features.Help;
using ClassicaCodex.Mac.Features.Instruments;
using ClassicaCodex.Mac.Features.Reactions;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Fictional Ancient Reactions, the three instruments, About, Help, Editor's
/// Notes and the preface, driven against the sample library and photographed
/// in both appearances.
///
/// The browser is never launched: every page and link these windows open
/// goes through ExternalLinks.Opener, which the tests replace with a list.
/// </summary>
public class ReactionsAndDocsTests
{
    private static async Task<MainWindow?> OpenLibraryAsync()
    {
        if (Screens.SampleLibrary is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var main = new MainWindow { Width = 1280, Height = 820 };
        main.Show();
        await Screens.Settle(10);
        return main;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe, int rounds = 80) where T : class
    {
        for (var i = 0; i < rounds; i++)
        {
            if (probe() is { } found) return found;
            await Screens.Settle(1);
        }

        throw new TimeoutException($"Nothing appeared: {typeof(T).Name}");
    }

    private static async Task InBothAppearancesAsync(TopLevel window, string name)
    {
        try
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Screens.Settle(4);
            Screens.Save(window, name + "-light");

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            await Screens.Settle(4);
            Screens.Save(window, name + "-dark");
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Screens.Settle(2);
        }
    }

    // ---- Fictional Ancient Reactions ------------------------------------------

    [AvaloniaFact]
    public async Task The_Iliad_debate_opens_with_the_fiction_label_first_and_cites_every_real_speaker()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var iliad = main.LibraryWorks.First(w => w.Title == "Iliad");
        var author = main.AuthorNameFor(iliad);
        Assert.True(ReactionsFeature.HasDebate(author, iliad));

        ReactionsFeature.Open(main, iliad, author);
        var window = await WaitForAsync(() => main.OwnedWindows.OfType<ReactionsWindow>().FirstOrDefault());
        await WaitForAsync(() => window.Views.Count > 0 ? window.Views : null);
        await Screens.Settle(4);

        // The label is the first thing in the window and says it is fiction.
        var root = (DockPanel)window.Content!;
        var banner = root.Children[0];
        Assert.Equal("FictionBanner", banner.Name);
        var bannerText = banner.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.StartsWith("Fictional reconstruction.", bannerText.Inlines!.Text);
        Assert.Contains("nobody said these words", bannerText.Inlines.Text);
        Assert.True(banner.Bounds.Bottom <= root.Children[1].Bounds.Top + 0.5);

        // Every turn a real person speaks carries its reference.
        var historical = window.Views.Where(v => v.Critic.Kind == CriticKind.Historical).ToList();
        Assert.NotEmpty(historical);
        Assert.All(historical, v => Assert.False(string.IsNullOrWhiteSpace(v.SourceLabel)));

        // The Iliad is in the sample library, so passages resolve to links.
        Assert.Contains(window.Views, v => v.PassageResolved);
        Assert.Contains("real", window.StatusText);

        await InBothAppearancesAsync(window, "reactions-iliad");

        // A picture of a scrolling list at the top is not a picture of a
        // scrolling list - the Windows lesson. Photograph it scrolled too.
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, 900);
        await Screens.Settle(4);
        Screens.Save(window, "reactions-iliad-scrolled-light");

        window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task A_critic_card_says_plainly_whether_the_speaker_existed()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var debate = ReactionLibrary.DebatesFor("Homer", "Iliad").First();
        var real = debate.SpeakerIds.Select(ReactionLibrary.Critic).First(c => c!.Kind == CriticKind.Historical)!;
        var invented = debate.SpeakerIds.Select(ReactionLibrary.Critic).First(c => c!.Kind == CriticKind.Composite)!;

        foreach (var (critic, name) in new[] { (real, "critic-card-real"), (invented, "critic-card-invented") })
        {
            var card = new CriticCardWindow(critic);
            _ = card.ShowDialog(main);
            await Screens.Settle(4);

            var text = string.Join(" ", card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(critic.Kind == CriticKind.Historical ? "A real person." : "Invented for this debate.", text);

            await InBothAppearancesAsync(card, name);
            card.Close();
        }

        var sheet = ReactionsWindow.BuildExplanationSheet();
        _ = sheet.ShowDialog(main);
        await Screens.Settle(4);
        Assert.Contains("These debates are fiction.", string.Join(" ", sheet.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        Screens.Save(sheet, "reactions-what-am-i-reading");
        sheet.Close();

        main.Close();
    }

    [AvaloniaFact]
    public async Task The_Republic_debate_links_its_sources_into_the_library()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var republic = main.LibraryWorks.First(w => w.Title == "Republic");
        ReactionsFeature.Open(main, republic, main.AuthorNameFor(republic));
        var window = await WaitForAsync(() => main.OwnedWindows.OfType<ReactionsWindow>().FirstOrDefault());
        await WaitForAsync(() => window.Views.Count > 0 ? window.Views : null);
        await Screens.Settle(4);

        // Plato is in the sample library, so a source citing him opens.
        Assert.Contains(window.Views, v => v.PassageResolved);
        Screens.Save(window, "reactions-republic-light");

        window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void A_work_with_no_debate_opens_nothing()
    {
        var work = new ClassicaCodex.Core.Models.Work { WorkId = -1, Title = "Anabasis", CtsUrn = "urn:none" };
        Assert.False(ReactionsFeature.HasDebate("Xenophon", work));
        Assert.Empty(ReactionLibrary.DebatesFor("Xenophon", "Anabasis"));
    }

    /// <summary>
    /// Every bubble has to stay readable in both appearances. The tint is the
    /// speaker's colour mixed toward the page, so one set of recipes serves
    /// both - which is only safe if the result contrasts with the ink drawn on
    /// it. The Mac's pages are not the Windows app's, so the Windows test of
    /// the same rule does not cover these: 4.5:1, the WCAG AA threshold for
    /// body text, against the Mac palette.
    /// </summary>
    [AvaloniaFact]
    public void Every_bubble_stays_readable_on_the_Mac_pages_in_both_appearances()
    {
        var failures = new List<string>();

        foreach (var (variant, amount) in new[] { (ThemeVariant.Light, 0.86), (ThemeVariant.Dark, 0.74) })
        {
            var page = Brush("Cc.Page", variant);
            var ink = Brush("Cc.Ink", variant);

            foreach (var critic in ReactionLibrary.Packs.SelectMany(p => p.Critics).DistinctBy(c => c.Id))
            {
                var fill = AncientAvatars.Mix(AncientAvatars.Signature(critic.Avatar), page, amount);
                var ratio = Contrast(fill, ink);
                if (ratio < 4.5) failures.Add($"{critic.Id} ({variant}): {ratio:F1}:1");
            }
        }

        Assert.True(failures.Count == 0, "bubble text would be hard to read:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// A recipe with nonsense in it must still produce a face: the packs are
    /// hand written and a reader may supply their own, so a mistyped hairstyle
    /// has to cost a hairstyle rather than throw while the window draws.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("skin:chartreuse;hair:magenta;beard:luxuriant;hat:sombrero")]
    [InlineData("cloth:not-a-colour;accent:#gggggg")]
    [InlineData("::::;;;;")]
    [InlineData("skin:tan;hair:grey;beard:full;hat:laurel;cloth:#7b5230;accent:#3f2a16")]
    [InlineData("skin:light;hair:dark;hat:veil;cloth:#8a4f6d")]
    public void A_portrait_draws_from_any_recipe(string? recipe)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(88, 88));
        using (var context = bitmap.CreateDrawingContext())
        {
            AncientAvatars.Draw(context, AncientAvatars.Parse(recipe), 88);
        }

        Assert.Equal(88, bitmap.PixelSize.Width);
    }

    /// <summary>
    /// Every critic's face on one sheet. The Windows portraits' first version
    /// drew every critic as a bald man in ear-muffs with no beard, and no
    /// assertion could have caught that - it took looking. This is the sheet
    /// to look at.
    /// </summary>
    [AvaloniaFact]
    public async Task Every_critic_has_a_face()
    {
        var critics = ReactionLibrary.Packs.SelectMany(p => p.Critics).DistinctBy(c => c.Id).ToList();
        Assert.NotEmpty(critics);

        var sheet = new WrapPanel { Margin = new Thickness(16) };
        foreach (var critic in critics)
        {
            var cell = new StackPanel { Width = 112, Margin = new Thickness(4, 4, 4, 10) };
            cell.Children.Add(new CriticPortrait(critic, 96) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock
            {
                Text = critic.Name,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            sheet.Children.Add(cell);
        }

        var window = new Window { Width = 1240, Height = 1000, Content = new ScrollViewer { Content = sheet } };
        window.Show();
        await Screens.Settle(4);
        Screens.Save(window, "reactions-portraits");
        window.Close();
    }

    // ---- The instruments ---------------------------------------------------------

    [AvaloniaFact]
    public async Task Each_instrument_card_opens_its_shipped_page_and_nothing_else()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        var opened = new List<string>();
        var previous = ExternalLinks.Opener;
        ExternalLinks.Opener = target =>
        {
            opened.Add(target);
            return true;
        };

        try
        {
            foreach (var (open, page, shot) in new (Action, string, string)[]
                     {
                         (() => InstrumentsFeature.OpenAntikythera(main), "Antikythera.html", "instrument-antikythera"),
                         (() => InstrumentsFeature.OpenAlmagest(main), "Almagest.html", "instrument-almagest"),
                         (() => InstrumentsFeature.OpenHarmonics(main), "Harmonics.html", "instrument-harmonics")
                     })
            {
                open();
                var card = await WaitForAsync(() => main.OwnedWindows.OfType<InstrumentCard>().FirstOrDefault());
                await Screens.Settle(4);

                // Nothing is opened until the button is pressed.
                Assert.Empty(opened);
                await InBothAppearancesAsync(card, shot);

                var scroller = card.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroller.Offset = new Vector(0, 99999);
                await Screens.Settle(3);
                Screens.Save(card, shot + "-sources-light");

                card.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Screens.Settle(2);

                var target = Assert.Single(opened);
                Assert.EndsWith(Path.Combine("Pages", page), target);
                Assert.True(File.Exists(target), $"{target} is not beside the app");

                opened.Clear();
                card.Close();
                await Screens.Settle(2);
            }
        }
        finally
        {
            ExternalLinks.Opener = previous;
        }

        main.Close();
    }

    /// <summary>
    /// On the Harmonics card the warning comes before the description, and the
    /// button itself says the page can make sound - someone who has read
    /// nothing else on the card has read the button.
    /// </summary>
    [AvaloniaFact]
    public async Task The_harmonics_card_warns_about_sound_before_anything_else()
    {
        var host = new Window { Width = 400, Height = 300 };
        host.Show();

        var card = new HarmonicsCard();
        card.Show(host);
        await Screens.Settle(4);

        Assert.Contains("sound", card.OpenButton.Content as string);

        var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        var caution = texts.FindIndex(t => t.Text == "This one makes sound.");
        var description = texts.FindIndex(t => t.Text!.StartsWith("Greek harmonic science", StringComparison.Ordinal));
        Assert.True(caution >= 0 && caution < description);

        card.Close();
        host.Close();
    }

    // ---- About and Help ---------------------------------------------------------------

    [AvaloniaFact]
    public async Task About_names_the_version_and_every_licence()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        AboutFeature.Open(main);
        var about = await WaitForAsync(() => main.OwnedWindows.OfType<AboutWindow>().FirstOrDefault());
        await Screens.Settle(4);

        var text = string.Join("\n", about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("Version 3.", text);
        Assert.Contains("License: Creative Commons Attribution-NonCommercial 4.0 International", text);
        Assert.Contains("never saves a copy", text);

        await InBothAppearancesAsync(about, "about");

        var scroller = about.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, 99999);
        await Screens.Settle(4);
        Screens.Save(about, "about-end-light");

        about.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void The_help_text_is_all_there_and_says_how_the_Mac_does_things()
    {
        Assert.Equal(27, HelpTopics.All.Length);

        foreach (var (title, body) in HelpTopics.All)
        {
            var text = HelpOutline.Expand(body);
            Assert.DoesNotContain("Ctrl", text);
            Assert.DoesNotContain("Alt+", text);
            Assert.DoesNotContain("%LocalAppData%", text);
            Assert.DoesNotContain("Setup Wizard", text);
            Assert.DoesNotMatch(@"%[A-Z_]+%", text);
            Assert.False(string.IsNullOrWhiteSpace(text), title);
        }

        // The outline reads as headings, paragraphs and lists.
        var reading = HelpOutline.Parse(HelpTopics.All.Single(t => t.Title == "Reading texts").Body);
        Assert.Contains(reading, b => b.Kind == HelpBlockKind.Heading && b.Text == "Marks at the end of a line");
        Assert.Contains(reading, b => b.Kind == HelpBlockKind.Item && b.Text.StartsWith("Copy Passage - ", StringComparison.Ordinal));
        Assert.Contains(reading, b => b.Kind == HelpBlockKind.Paragraph && b.Text.EndsWith("a line for:", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Help_shows_every_topic_and_searches_them()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        HelpFeature.Open(main);
        var help = await WaitForAsync(() => main.OwnedWindows.OfType<HelpWindow>().FirstOrDefault());
        await Screens.Settle(4);

        Assert.Equal(27, help.TopicCount);
        Assert.Equal("Getting started", help.ShownTopic);
        await InBothAppearancesAsync(help, "help-getting-started");

        help.ShowTopic("Reading texts");
        await Screens.Settle(4);
        Screens.Save(help, "help-reading-texts-light");

        help.ShowTopic("Where your data lives");
        await Screens.Settle(4);
        Screens.Save(help, "help-data-light");

        help.SetSearch("keychain");
        await Screens.Settle(4);
        Assert.True(help.TopicCount is > 0 and < 27);
        Assert.Equal("Translate", help.ShownTopic);
        Screens.Save(help, "help-search-light");

        var opened = new List<string>();
        var previous = ExternalLinks.Opener;
        ExternalLinks.Opener = target =>
        {
            opened.Add(target);
            return true;
        };
        try
        {
            var report = help.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("link"));
            report.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Screens.Settle(2);
            Assert.Equal("https://github.com/ClassicaCodex/ClassicaCodex/issues", Assert.Single(opened));
        }
        finally
        {
            ExternalLinks.Opener = previous;
        }

        help.Close();
        main.Close();
    }

    // ---- Editor's Notes and the preface -------------------------------------------------

    [AvaloniaFact]
    public async Task Editors_notes_come_from_the_clicked_pages_edition()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        // Shorey's Republic: a translation with its own notes, which is the
        // case the clicked-page rule exists for.
        var editions = await new EditionRepository().GetByWorkAsync(main.LibraryWorks.First(w => w.Title == "Republic").WorkId);
        var shorey = editions.First(e => e.CtsUrn.EndsWith("perseus-eng2", StringComparison.Ordinal));
        var nodes = await new TextNodeRepository().GetByEditionAsync(shorey.EditionId);
        var first = nodes.First(n => n.CitationRef.EndsWith(".1.327.1", StringComparison.Ordinal));

        ApparatusFeature.Open(main, new PassageContext(first, shorey, null, false, "Georgia, DejaVu Serif, serif", "Paul Shorey", null));
        var notes = await WaitForAsync(() => main.OwnedWindows.OfType<EditorNotesWindow>().FirstOrDefault());
        await WaitForAsync(() => notes.Entries.Count > 0 ? notes.Entries : null);
        await Screens.Settle(4);

        Assert.EndsWith("for this line.", notes.StatusText);
        Assert.Contains("Editor's note", notes.DetailText);
        await InBothAppearancesAsync(notes, "editors-notes-line");

        await notes.ShowWholeEditionAsync();
        await Screens.Settle(4);
        Assert.Contains("entries across Paul Shorey", notes.StatusText);
        Screens.Save(notes, "editors-notes-edition-light");
        notes.Close();

        // An edition with no notes at all says so, rather than looking broken.
        var bare = (await new EditionRepository().GetByWorkAsync(main.LibraryWorks.First(w => w.Title == "Iliad").WorkId)).First();
        var bareNode = (await new TextNodeRepository().GetByEditionAsync(bare.EditionId)).First();
        if (await new ApparatusRepository().CountForEditionAsync(bare.EditionId) == 0)
        {
            ApparatusFeature.Open(main, new PassageContext(bareNode, bare, null, true, "Palatino, DejaVu Serif, serif", "Homer, Iliad", null));
            var empty = await WaitForAsync(() => main.OwnedWindows.OfType<EditorNotesWindow>().FirstOrDefault());
            await WaitForAsync(() => empty.StatusText.Length > 0 ? empty.StatusText : null);
            Assert.Contains("carries no editor's notes", empty.StatusText);
            Screens.Save(empty, "editors-notes-none-light");
            empty.Close();
        }

        main.Close();
    }

    [AvaloniaFact]
    public async Task A_preface_opens_as_a_page_to_read()
    {
        if (await OpenLibraryAsync() is not { } main) return;

        const string text =
            "The translation which follows was made from the text of the Oxford editors, and departs from it only where a "
            + "reading of the manuscripts seemed plainly better.\r\n\r\nNo translation can do the work of the original, and this "
            + "one does not pretend to: it is meant to be read beside the Greek, a page at a time, by somebody learning to read "
            + "the Greek for themselves.";

        ApparatusFeature.ShowPreface(main, "Preface — Samuel Butler", text);
        var preface = await WaitForAsync(() => main.OwnedWindows.OfType<PrefaceWindow>().FirstOrDefault());
        await Screens.Settle(4);

        Assert.Equal("Preface — Samuel Butler", preface.Title);
        Assert.DoesNotContain("\r", preface.Text);
        await InBothAppearancesAsync(preface, "preface");

        preface.Close();
        main.Close();
    }

    private static Color Brush(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"{key} is not a brush in {variant}");

    /// <summary>Relative luminance contrast, as WCAG defines it.</summary>
    private static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color colour) =>
        0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);

    private static double Channel(byte value)
    {
        var v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
