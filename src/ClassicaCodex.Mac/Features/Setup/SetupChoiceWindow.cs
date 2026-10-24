using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>The two ways into setup.</summary>
public enum SetupMode
{
    Guided,
    Advanced
}

/// <summary>
/// Asked when Setup is opened, so a first-time or non-technical reader lands
/// in the guided assistant by default without the choice being hidden from
/// someone who already knows they want everything on one screen - the
/// Windows SetupModeChoiceForm.
///
/// Guided is the default button, as it is on Windows, and the obvious one on
/// an empty library, which is what this says when it is opened on one -
/// straight after a new library is created, most often.
/// </summary>
public sealed class SetupChoiceWindow : Window
{
    private SetupMode? _choice;
    private readonly Button _guided;
    private readonly Button _advanced;

    public SetupChoiceWindow(bool libraryIsEmpty)
    {
        Title = "Setup";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _guided = Choice(
            "setup", "Setup", "Guided Setup", "Recommended",
            "One step at a time, plain language, no file paths or technical detail. The right choice for a first-time "
            + "setup, or anyone who'd rather not think about where things go.",
            SetupMode.Guided);
        _guided.IsDefault = true;

        _advanced = Choice(
            "settings", "Sliders", "Advanced Setup", null,
            "Everything on one screen - repository addresses, destination folders, manual ingest. Better if you already "
            + "have the files downloaded, or want more control over where they go.",
            SetupMode.Advanced);

        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        cards.Children.Add(_guided);
        cards.Children.Add(_advanced);

        var cancel = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Cancel", MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => Close();

        var layout = new StackPanel { Margin = new Thickness(26, 22, 26, 18), Spacing = 14 };
        layout.Children.Add(new TextBlock { Text = "How would you like to set things up?", Classes = { "h1" } });
        layout.Children.Add(new Border { MaxWidth = 594, HorizontalAlignment = HorizontalAlignment.Left, Child = SetupLook.Text(
            libraryIsEmpty
                ? "Your library is empty, so Guided Setup is the place to start: it begins with where the library and the downloads go, then takes the collections one at a time."
                : "Your library already has texts in it. Either way shows what is installed and what is still to come, and installs nothing until you press its button.",
            "muted") });
        layout.Children.Add(cards);
        layout.Children.Add(cancel);
        Content = layout;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };

        Opened += (_, _) => _guided.Focus();
    }

    /// <summary>Asks, and returns the choice, or null if the window was closed without one.</summary>
    public static async Task<SetupMode?> AskAsync(Window owner, bool libraryIsEmpty)
    {
        var window = new SetupChoiceWindow(libraryIsEmpty);
        await window.ShowDialog(owner);
        return window._choice;
    }

    internal void Choose(SetupMode mode)
    {
        _choice = mode;
        Close();
    }

    private Button Choice(string tileKey, string glyph, string title, string? badge, string description, SetupMode mode)
    {
        var tile = Art.Tile(tileKey, glyph, 52);
        tile.HorizontalAlignment = HorizontalAlignment.Left;

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 6) };
        heading.Children.Add(new TextBlock { Text = title, Classes = { "h2" }, VerticalAlignment = VerticalAlignment.Center });
        if (badge != null)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badge, FontSize = 11, FontWeight = FontWeight.SemiBold }
            };
            pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable("Cc.AccentSoft"));
            ((TextBlock)pill.Child).Bind(TextBlock.ForegroundProperty, pill.GetResourceObservable("Cc.Accent"));
            heading.Children.Add(pill);
        }

        var content = new StackPanel();
        content.Children.Add(tile);
        content.Children.Add(heading);
        content.Children.Add(SetupLook.Text(description, "muted"));

        var button = new Button
        {
            Classes = { "tile" },
            Content = content,
            Width = 290,
            MinHeight = 210,
            Padding = new Thickness(18, 16),
            VerticalContentAlignment = VerticalAlignment.Top
        };
        button.Click += (_, _) => Choose(mode);
        return button;
    }
}
