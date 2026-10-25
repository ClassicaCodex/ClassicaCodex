using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// Asks for one line of text: a name for a translation, a tag, a title.
/// The Mac counterpart of the Windows TextPromptForm.
/// </summary>
public static class TextPrompt
{
    /// <returns>The text entered, or null if the reader cancelled.</returns>
    public static async Task<string?> Ask(
        Window owner, string title, string prompt, string initial = "", string confirm = "OK", bool multiline = false)
    {
        string? result = null;

        var box = new TextBox
        {
            Text = initial,
            MinWidth = 360,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap,
            Height = multiline ? 110 : double.NaN
        };

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var ok = new Button { Content = confirm, Classes = { "accent" }, IsDefault = !multiline, MinWidth = 84 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        ok.Click += (_, _) => { result = box.Text ?? string.Empty; window.Close(); };
        cancel.Click += (_, _) => window.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var layout = new StackPanel { Margin = new Thickness(22, 18), Spacing = 12, MaxWidth = 460 };
        layout.Children.Add(new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        layout.Children.Add(box);
        layout.Children.Add(buttons);
        window.Content = layout;

        window.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        await window.ShowDialog(owner);
        return result;
    }
}
