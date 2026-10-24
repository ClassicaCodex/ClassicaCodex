using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// "Tag This Line": the line being tagged, a name for the tag and an
/// optional category - the Windows TagPromptForm as a Mac sheet.
///
/// The category is free text, and it is what the Myth Network's shapes are
/// assigned by, so it is worth a word in the prompt about what goes there.
/// </summary>
internal static class TagPrompt
{
    /// <returns>The name and category, or null if the reader cancelled.</returns>
    public static async Task<(string Name, string? Category)?> AskAsync(Window owner, string previewText)
    {
        (string, string?)? result = null;

        var window = new Window
        {
            Title = "Tag This Line",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var name = new TextBox { Width = 420, PlaceholderText = "Prometheus" };
        var category = new TextBox { Width = 420, PlaceholderText = "character, theme, god, place…" };

        var ok = new Button { Content = "Tag It", Classes = { "accent" }, IsDefault = true, MinWidth = 84, IsEnabled = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };

        // A tag needs a name; Windows let OK through with none and then did
        // nothing, which here is simply a button that waits for one.
        name.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);

        ok.Click += (_, _) =>
        {
            var tagName = (name.Text ?? string.Empty).Trim();
            if (tagName.Length == 0) return;
            result = (tagName, string.IsNullOrWhiteSpace(category.Text) ? null : category.Text.Trim());
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var layout = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 6, Width = 420 };
        layout.Children.Add(Preview(previewText));
        layout.Children.Add(new TextBlock { Text = "Tag name (e.g. “Prometheus”):", Margin = new Thickness(0, 10, 0, 0) });
        layout.Children.Add(name);
        layout.Children.Add(new TextBlock { Text = "Category (optional - e.g. “character”, “theme”):", Margin = new Thickness(0, 8, 0, 0) });
        layout.Children.Add(category);
        layout.Children.Add(buttons);
        window.Content = layout;

        window.Opened += (_, _) => name.Focus();

        await window.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// The line itself, cut where the Windows prompt cuts it: enough to be
    /// sure it is the right line, not so much the prompt becomes a page.
    /// </summary>
    internal static Control Preview(string text)
    {
        var shown = text.Length > 160 ? text[..160] + "…" : text;

        var block = new TextBlock
        {
            Text = shown,
            FontFamily = PassageResults.FontFor(text),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "muted" }
        };

        return new Border { Classes = { "panel" }, Padding = new Thickness(12, 9), Child = block };
    }
}
