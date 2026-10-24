using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features;

/// <summary>
/// What a tool shows while its Mac window is still being written: its tile,
/// its name, and a plain statement that it exists on Windows and is on its
/// way here. Better than a menu item that does nothing, and honest about why.
/// </summary>
public static class ComingSoon
{
    public static void Show(IReaderHost host, string title, string tileKey, string glyph, string? windowsName = null)
    {
        var window = new FeatureWindow
        {
            Title = title,
            Width = 460,
            Height = 300,
            MinWidth = 380,
            MinHeight = 260,
            CanResize = false
        };

        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var tile = Art.Tile(tileKey, glyph, 72);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(tile);
        stack.Children.Add(new TextBlock { Text = title, Classes = { "h1" }, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = $"{windowsName ?? title} is in the Windows app and is still being brought across to the Mac. "
                   + "Everything it reads is already in your library.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Classes = { "muted" },
            MaxWidth = 340
        });

        window.Content = stack;
        window.Show(host.Window);
    }
}
