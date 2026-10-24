using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Features.Tags;

namespace ClassicaCodex.Mac.Features.Bookmarks;

/// <summary>
/// "Bookmark This Line": the line, and an optional note - the Windows
/// BookmarkPromptForm as a Mac sheet.
///
/// The note takes Return as a new line, as it does on Windows, since a note
/// is often more than one; ⌘Return bookmarks.
/// </summary>
internal static class BookmarkPrompt
{
    /// <returns>(true, note) to bookmark - the note null when left empty - or (false, null) if cancelled.</returns>
    public static async Task<(bool Confirmed, string? Note)> AskAsync(Window owner, string previewText)
    {
        var confirmed = false;
        string? note = null;

        var window = new Window
        {
            Title = "Bookmark This Line",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var noteBox = new TextBox
        {
            Width = 420,
            Height = 120,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };

        var ok = new Button { Content = "Bookmark It", Classes = { "accent" }, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };

        void Confirm()
        {
            confirmed = true;
            note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim();
            window.Close();
        }

        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => window.Close();

        noteBox.KeyDown += (_, e) =>
        {
            if (PassageResults.IsCommand(e, Key.Enter))
            {
                e.Handled = true;
                Confirm();
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var layout = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 6, Width = 420 };
        layout.Children.Add(TagPrompt.Preview(previewText));
        layout.Children.Add(new TextBlock { Text = "Note (optional):", Margin = new Thickness(0, 10, 0, 0) });
        layout.Children.Add(noteBox);
        layout.Children.Add(new TextBlock { Text = "⌘Return to bookmark", Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Left });
        layout.Children.Add(buttons);
        window.Content = layout;

        window.Opened += (_, _) => noteBox.Focus();

        await window.ShowDialog(owner);
        return (confirmed, note);
    }
}
