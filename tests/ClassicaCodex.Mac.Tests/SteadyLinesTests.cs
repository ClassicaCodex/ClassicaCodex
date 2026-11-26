using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// Lines that keep their distance, and a reader whose line height follows its
/// text size. On Windows a Greek line holding an elision mark, which Palatino
/// Linotype lacks, used to drop into the line under it.
/// </summary>
public class SteadyLinesTests
{
    // ἵνʼ on the first line, with U+02BC: Perseus's elision mark.
    private const string Elided =
        "παραιτήσεται καθάπερ ὑμεῖς· ἵνʼ οὖν ἑτέραν ἀρχὴν ἐκπορίζηται καὶ μὴ τὴν αὐτὴν ἀναγκασθῇ λέγειν, ὡς ὑπαρχούσης αὐτῷ συγγνώμης εἰς τότε οὕτω λεγέτω.";

    // The same words with no character the reading face could lack.
    private const string Plain =
        "παραιτήσεται καθάπερ ὑμεῖς· ἵνα οὖν ἑτέραν ἀρχὴν ἐκπορίζηται καὶ μὴ τὴν αὐτὴν ἀναγκασθῇ λέγειν, ὡς ὑπαρχούσης αὐτῷ συγγνώμης εἰς τότε οὕτω λεγέτω.";

    private const double Size = 16;

    private static TextBlock Laid(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.FontSize = Size;
        block.LineHeight = Size * 1.38;
        block.Width = 380;
        block.Measure(new Size(380, double.PositiveInfinity));
        block.Arrange(new Rect(block.DesiredSize));
        return block;
    }

    [AvaloniaFact]
    public void A_borrowed_glyph_leaves_every_line_on_the_same_baseline()
    {
        var block = new TextBlock();
        SteadyLines.SetText(block, Elided, SteadyLines.Face("Cc.SourceFont"));
        Laid(block);

        var baselines = block.TextLayout.TextLines.Select(line => line.Baseline).ToList();
        Assert.True(baselines.Count >= 3, $"Expected the passage to wrap, got {baselines.Count} lines.");
        Assert.All(baselines, baseline => Assert.Equal(baselines[0], baseline, 2));
    }

    [AvaloniaFact]
    public void A_steadied_line_lands_where_the_reading_face_alone_puts_it()
    {
        var face = SteadyLines.Face("Cc.SourceFont");
        var plain = Laid(new TextBlock { Text = Plain, FontFamily = face });
        var steady = new TextBlock();
        SteadyLines.SetText(steady, Elided, face);
        Laid(steady);

        // Where the first line's baseline falls on screen: the lift is a
        // negative top margin, the rest is the layout's own.
        var plainBaseline = plain.TextLayout.TextLines[0].Baseline;
        var steadyBaseline = steady.Margin.Top + steady.TextLayout.TextLines[0].Baseline;
        Assert.Equal(plainBaseline, steadyBaseline, 0.75);

        // And the row is as tall as it was.
        Assert.Equal(0, steady.Margin.Top + steady.Margin.Bottom, 3);
    }

    [AvaloniaFact]
    public void Filling_a_block_again_lifts_it_once()
    {
        var face = SteadyLines.Face("Cc.SourceFont");
        var block = new TextBlock { FontSize = Size, LineHeight = Size * 1.38, Margin = new Thickness(0, 6, 0, 0) };
        SteadyLines.SetText(block, Elided, face);
        var once = block.Margin;
        SteadyLines.SetText(block, Plain, face);
        SteadyLines.SetText(block, Elided, face);

        Assert.Equal(once, block.Margin);
        Assert.Equal(Elided, SteadyLines.TextOf(block));
    }

    [AvaloniaFact]
    public async Task Reader_lines_follow_the_text_size_and_keep_their_distance()
    {
        if (Screens.SampleLibrary is not { } library) return;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(8);

        // Plato's Critias, whose speeches are full of elided words; the
        // Agamemnon if the sample has no Plato.
        var work = window.LibraryWorks.FirstOrDefault(w => w.Title == "Critias")
                   ?? window.LibraryWorks.First(w => w.Title.Contains("Agamemnon"));
        Assert.True(await window.OpenWorkAsync(work.WorkId));
        await Screens.Settle(10);
        Screens.Save(window, "reader-steady-lines");

        var original = window.GetVisualDescendants().OfType<ReaderPane>().First(p => p.Role == PaneRole.Original);
        var rows = original.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Inlines is { Count: > 0 } && t.TextLayout.TextLines.Count > 1)
            .ToList();
        Assert.NotEmpty(rows);

        var size = ReadingFontSettings.SourceSize * 96.0 / 72.0;
        foreach (var row in rows.Where(r => !r.IsSet(TextBlock.FontSizeProperty)))
        {
            Assert.Equal(size * 1.38, row.LineHeight, 2);

            var baselines = row.TextLayout.TextLines.Select(line => line.Baseline).ToList();
            Assert.All(baselines, baseline => Assert.Equal(baselines[0], baseline, 2));
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task A_library_that_is_not_there_is_named_with_Retry()
    {
        var missing = Path.Combine(Path.GetTempPath(), "cc-unplugged-drive", Guid.NewGuid().ToString("N"), "classicacodex.db");

        await WithRememberedLibrary(missing, async () =>
        {
            await LaunchLibrary.OpenRememberedAsync();
            Assert.Equal(missing, LaunchLibrary.Missing);
            Assert.Null(LaunchLibrary.Failed);
            Assert.False(DbConnectionFactory.IsConfigured);

            var window = new MainWindow { Width = 1000, Height = 700 };
            window.Show();
            await Screens.Settle(8);

            var alert = Assert.Single(window.OwnedWindows);
            var message = alert.GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text!;
            Assert.Contains(missing, message);
            Assert.Contains("isn't available right now", message);
            Assert.Contains(alert.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Retry"));
            Screens.Save(alert, "library-not-found");

            alert.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Screens.Settle(6);

            Assert.Empty(window.OwnedWindows);
            Assert.True(window.GetVisualDescendants().OfType<NoLibraryView>().Single().IsVisible);
            Assert.Null(LaunchLibrary.Missing);
            window.Close();
        });
    }

    [AvaloniaFact]
    public async Task A_library_that_will_not_open_is_reported_and_not_left_half_open()
    {
        var junk = Path.Combine(Path.GetTempPath(), "cc-junk-" + Guid.NewGuid().ToString("N") + ".db");
        await File.WriteAllTextAsync(junk, "This is not a database, and SQLite will say so.");

        try
        {
            await WithRememberedLibrary(junk, async () =>
            {
                await LaunchLibrary.OpenRememberedAsync();
                Assert.Equal(junk, LaunchLibrary.Failed?.Path);
                Assert.Null(LaunchLibrary.Missing);
                Assert.False(DbConnectionFactory.IsConfigured);

                var window = new MainWindow { Width = 1000, Height = 700 };
                window.Show();
                await Screens.Settle(8);

                var alert = Assert.Single(window.OwnedWindows);
                var message = alert.GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text!;
                Assert.Contains(junk, message);
                Assert.Contains("couldn't be opened", message);
                alert.Close();
                await Screens.Settle(4);

                Assert.True(window.GetVisualDescendants().OfType<NoLibraryView>().Single().IsVisible);
                window.Close();
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(junk);
        }
    }

    /// <summary>
    /// Runs <paramref name="test"/> with <paramref name="path"/> as the
    /// remembered library, then puts the preference back as it was.
    /// </summary>
    private static async Task WithRememberedLibrary(string path, Func<Task> test)
    {
        var preference = Path.Combine(PreferencesFolder.Root, "database-path.txt");
        var before = File.Exists(preference) ? await File.ReadAllTextAsync(preference) : null;

        Screens.ForgetLibrary();
        Directory.CreateDirectory(PreferencesFolder.Root);
        await File.WriteAllTextAsync(preference, path);

        try
        {
            await test();
        }
        finally
        {
            LaunchLibrary.Clear();
            Screens.ForgetLibrary();
            if (before == null) File.Delete(preference);
            else await File.WriteAllTextAsync(preference, before);
        }
    }
}
