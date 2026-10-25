using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// Every tool as a painted tile, grouped as the sidebar groups them - the
/// Windows toolbar's eighteen illustrated buttons, given room to be seen.
/// </summary>
public sealed class ToolGalleryWindow : FeatureWindow
{
    public ToolGalleryWindow(MainWindow main)
    {
        Title = "All Tools";
        Width = 900;
        Height = 700;

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 28), Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = "All Tools", Classes = { "h1" } });
        stack.Children.Add(new TextBlock
        {
            Text = "Everything Classica Codex can do. The ones marked “open work” act on the work in the reader.",
            Classes = { "muted" },
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var section in ToolCatalog.All.GroupBy(t => t.Section))
        {
            stack.Children.Add(new TextBlock { Text = ToolCatalog.Title(section.Key), Classes = { "section" }, Margin = new Thickness(2, 14, 0, 6) });

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var tool in section)
            {
                var tile = Art.Tile(tool.TileKey, tool.Glyph, 84);
                tile.HorizontalAlignment = HorizontalAlignment.Left;

                var text = new StackPanel { Spacing = 2, Margin = new Thickness(0, 10, 0, 0) };
                text.Children.Add(new TextBlock { Text = tool.Title, Classes = { "h3" }, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock
                {
                    Text = tool.NeedsWork ? tool.Subtitle + " · open work" : tool.Subtitle,
                    Classes = { "caption" },
                    TextWrapping = TextWrapping.Wrap
                });

                var content = new StackPanel();
                content.Children.Add(tile);
                content.Children.Add(text);

                var button = new Button
                {
                    Classes = { "tile" },
                    Content = content,
                    Width = 196,
                    Height = 190,
                    Margin = new Thickness(0, 0, 12, 12),
                    VerticalContentAlignment = VerticalAlignment.Top
                };

                var key = tool.Key;
                button.Click += (_, _) =>
                {
                    Close();
                    main.OpenTool(key);
                };
                wrap.Children.Add(button);
            }

            stack.Children.Add(wrap);
        }

        Content = new ScrollViewer { Content = stack };
    }
}
