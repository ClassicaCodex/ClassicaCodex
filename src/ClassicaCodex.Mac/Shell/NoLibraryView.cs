using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// The first thing a new Mac sees: there is no library yet, and two ways to
/// have one - open a library already built, on this Mac or copied from the
/// PC, or start an empty one and let setup fill it.
///
/// It is also what a reader sees whose library didn't open at launch, in place
/// of the two message boxes the Windows app shows. That is a different thing
/// to be told, so the page names the file and offers Retry, and the same two
/// buttons come with a warning.
/// </summary>
public sealed class NoLibraryView : UserControl
{
    private const string Title = "Choose your library";

    private const string Intro =
        "Classica Codex keeps everything - the texts, the dictionaries, your tags and bookmarks - in one library file on your own machine. "
        + "After that, reading and searching happen here, with no connection needed.";

    private static string CopyNote => SystemWords.Say("Already have a library on the PC? It is one file, and the Mac reads it as it is - no setup, no re-download. "
        + "On Windows it is classicacodex.db in C:\\Users\\<you>\\AppData\\Local\\ClassicaCodex, unless you chose "
        + "another folder during setup. Copy it across (AirDrop, a USB drive, a shared folder) and choose Open Library.");

    private static string ChoosingAnother => SystemWords.Say("Open Library and Create a New Library are for pointing at a different library or making a new one. "
        + "Your existing library is not touched either way, but this app will look for the one you choose from now on.");

    private readonly TextBlock _heading;
    private readonly TextBlock _intro;
    private readonly SelectableTextBlock _path;
    private readonly SelectableTextBlock _failure;
    private readonly TextBlock _triedAgain = new() { Text = "Tried again, with the same result.", Classes = { "muted" }, IsVisible = false };
    private readonly TextBlock _note;
    private readonly Button _open;
    private readonly Button _retry;

    public NoLibraryView(MainWindow main)
    {
        // Codex's empty scroll-rack, with one scroll waiting beside it; the
        // library medallion if that painting is not there.
        Control tile = Art.Load("empty-library.png") is { } painting
            ? new Image { Source = painting, Width = 240, Height = 180, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-16, 0, 0, -8) }
            : Art.Tile("authors", "Library", 84);
        tile.HorizontalAlignment = HorizontalAlignment.Left;

        _retry = new Button { Content = "Retry", Classes = { "accent" }, MinWidth = 100, IsVisible = false };
        _retry.Click += async (_, _) =>
        {
            var before = main.LibraryProblem;
            _retry.IsEnabled = false;
            await main.RetryLibraryAsync();
            _retry.IsEnabled = true;
            _triedAgain.IsVisible = main.LibraryProblem == before;
        };

        _open = new Button { Content = "Open Library…", Classes = { "accent" }, MinWidth = 150 };
        _open.Click += async (_, _) => await main.OpenLibraryAsync();

        var create = new Button { Content = "Create a New Library…", MinWidth = 150 };
        create.Click += async (_, _) => await main.CreateLibraryAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(_retry);
        buttons.Children.Add(_open);
        buttons.Children.Add(create);

        _note = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = CopyNote };
        var note = new Border
        {
            Classes = { "notice" },
            Margin = new Thickness(0, 22, 0, 0),
            Child = _note
        };

        _heading = new TextBlock { Text = Title, Classes = { "display" }, FontSize = 34, Margin = new Thickness(0, 10, 0, 0) };
        _intro = new TextBlock { Text = Intro, TextWrapping = TextWrapping.Wrap, FontSize = 14, Classes = { "muted" } };
        _path = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, IsVisible = false };
        _failure = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, IsVisible = false };

        var stack = new StackPanel { Spacing = 12, MaxWidth = 560, Margin = new Thickness(56), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(tile);
        stack.Children.Add(_heading);
        stack.Children.Add(_intro);
        stack.Children.Add(_path);
        stack.Children.Add(_failure);
        stack.Children.Add(buttons);
        stack.Children.Add(_triedAgain);
        stack.Children.Add(note);

        Content = new ScrollViewer { Content = stack };
    }

    /// <summary>The page for a library that didn't open, or the general one with no problem to report.</summary>
    internal void Show(LibraryProblem? problem)
    {
        _heading.Text = problem == null ? Title
            : problem.Failure == null ? "Your library isn't available"
            : "Your library couldn't be opened";

        _intro.Text = problem == null ? Intro
            : problem.Failure == null ? "Classica Codex can't reach your library, at the path below. Most often that is a drive that isn't plugged in. Plug it in and choose Retry."
            : "Classica Codex found your library, at the path below, but couldn't open it. If another program has it open, close that and choose Retry.";

        _path.Text = problem?.Path;
        _path.IsVisible = problem != null;
        _failure.Text = problem?.Failure;
        _failure.IsVisible = problem?.Failure != null;

        _retry.IsVisible = problem != null;
        _open.Classes.Set("accent", problem == null);
        _note.Text = problem == null ? CopyNote : ChoosingAnother;
    }
}
