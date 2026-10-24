using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// The reading behaviours the Windows reader is careful about, checked on
/// the Mac one: linked pages follow each other by passage, a jump lands on
/// its line, and Back returns to where the reader was.
/// </summary>
public class ReaderBehaviourTests
{
    private static async Task<MainWindow?> OpenSampleAsync()
    {
        if (Screens.SampleLibrary is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(8);
        return window;
    }

    private static ReaderPane[] Panes(MainWindow window) =>
        window.GetVisualDescendants().OfType<ReaderPane>().ToArray();

    [AvaloniaFact]
    public async Task Linked_pages_scroll_to_the_same_passage()
    {
        if (await OpenSampleAsync() is not { } window) return;

        var iliad = window.LibraryWorks.First(w => w.Title.Contains("Iliad"));
        Assert.True(await window.OpenWorkAsync(iliad.WorkId));
        await Screens.Settle(8);

        var panes = Panes(window);
        Assert.Equal(2, panes.Length);
        var original = panes.Single(p => p.Role == PaneRole.Original);
        var translation = panes.Single(p => p.Role == PaneRole.Translation);
        Assert.True(original.Count > 100);
        Assert.True(translation.Count > 0);

        // Scroll the Greek a long way down and let the other page follow.
        var target = Math.Min(200, Math.Min(original.Count, translation.Count) - 1);
        original.ScrollToTop(target);
        await Screens.Settle(8);

        Assert.Equal(original.TopIndex, translation.TopIndex);
        Screens.Save(window, "10-linked-scroll");
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_jump_lands_on_its_line_and_Back_returns()
    {
        if (await OpenSampleAsync() is not { } window) return;

        var agamemnon = window.LibraryWorks.First(w => w.Title.Contains("Agamemnon"));
        Assert.True(await window.OpenWorkAsync(agamemnon.WorkId));
        await Screens.Settle(6);

        // A line of the Aeneid, found the way a search result would carry it.
        var aeneid = window.LibraryWorks.First(w => w.Title.Contains("Aeneid"));
        var editions = await new EditionRepository().GetByWorkAsync(aeneid.WorkId);
        var latin = editions.First(e => e.Language == "lat");
        var nodes = await new TextNodeRepository().GetByEditionAsync(latin.EditionId);
        var line = nodes[Math.Min(500, nodes.Count - 1)];

        await window.NavigateToPassageAsync(aeneid.WorkId, line.TextNodeId);
        await Screens.Settle(8);

        Assert.Equal(aeneid.WorkId, window.OpenWork?.WorkId);
        var original = Panes(window).Single(p => p.Role == PaneRole.Original);
        Assert.Equal(line.TextNodeId, original.SelectedNode?.TextNodeId);
        Screens.Save(window, "11-jump-to-line");

        // ⌘[ - Back to the Agamemnon.
        window.OpenTool("search"); // any tool opening must not disturb history
        foreach (var owned in window.OwnedWindows.ToList()) owned.Close();

        var back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BackButton");
        Assert.True(back.IsEnabled);
        back.Command?.Execute(null);
        back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Screens.Settle(8);

        Assert.Equal(agamemnon.WorkId, window.OpenWork?.WorkId);
        window.Close();
    }
}
