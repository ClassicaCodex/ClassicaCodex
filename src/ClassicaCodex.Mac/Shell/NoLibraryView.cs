using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// The first thing a new Mac sees: there is no library yet, and two ways to
/// have one - open a library already built, on this Mac or copied from the
/// PC, or start an empty one and let setup fill it.
/// </summary>
public sealed class NoLibraryView : UserControl
{
    public NoLibraryView(MainWindow main)
    {
        // Codex's empty scroll-rack, with one scroll waiting beside it; the
        // library medallion if that painting is not there.
        Control tile = Art.Load("empty-library.png") is { } painting
            ? new Image { Source = painting, Width = 240, Height = 180, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-16, 0, 0, -8) }
            : Art.Tile("authors", "Library", 84);
        tile.HorizontalAlignment = HorizontalAlignment.Left;

        var open = new Button { Content = "Open Library…", Classes = { "accent" }, MinWidth = 150 };
        open.Click += async (_, _) => await main.OpenLibraryAsync();

        var create = new Button { Content = "Create a New Library…", MinWidth = 150 };
        create.Click += async (_, _) => await main.CreateLibraryAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(open);
        buttons.Children.Add(create);

        var copyNote = new Border
        {
            Classes = { "notice" },
            Margin = new Thickness(0, 22, 0, 0),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Already have a library on the PC? It is one file, and the Mac reads it as it is - no setup, no re-download. "
                       + "On Windows it is classicacodex.db in C:\\Users\\<you>\\AppData\\Local\\ClassicaCodex, unless you chose "
                       + "another folder during setup. Copy it across (AirDrop, a USB drive, a shared folder) and choose Open Library."
            }
        };

        var stack = new StackPanel { Spacing = 12, MaxWidth = 560, Margin = new Thickness(56), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(tile);
        stack.Children.Add(new TextBlock { Text = "Choose your library", Classes = { "display" }, FontSize = 34, Margin = new Thickness(0, 10, 0, 0) });
        stack.Children.Add(new TextBlock
        {
            Text = "Classica Codex keeps everything - the texts, the dictionaries, your tags and bookmarks - in one library file on your own machine. "
                   + "After that, reading and searching happen here, with no connection needed.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Classes = { "muted" }
        });
        stack.Children.Add(buttons);
        stack.Children.Add(copyNote);

        Content = new ScrollViewer { Content = stack };
    }
}
