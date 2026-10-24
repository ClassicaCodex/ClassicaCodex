using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Common;

public enum AlertKind { Info, Warning, Error, Question }

/// <summary>
/// The Mac's version of MessageBox: a small sheet-like window with a glyph,
/// a bold title, the message and buttons on the right, the default one last
/// and in the accent colour, as macOS lays them out.
///
/// The message can be selected and copied, because the useful ones are error
/// text somebody will want to paste into an issue.
/// </summary>
public static class Alert
{
    public static Task Info(Window? owner, string title, string message) =>
        Show(owner, title, message, AlertKind.Info, "OK");

    public static Task Warn(Window? owner, string title, string message) =>
        Show(owner, title, message, AlertKind.Warning, "OK");

    public static Task Error(Window? owner, string title, string message) =>
        Show(owner, title, message, AlertKind.Error, "OK");

    /// <summary>True if the reader chose <paramref name="confirm"/>.</summary>
    public static async Task<bool> Confirm(
        Window? owner, string title, string message, string confirm = "OK", string cancel = "Cancel",
        AlertKind kind = AlertKind.Question) =>
        await Show(owner, title, message, kind, cancel, confirm) == 1;

    /// <summary>
    /// Shows the alert and returns the index of the button chosen, or -1 if
    /// it was dismissed. The last button is the default (Return); the first is
    /// the cancel button (Escape) when there is more than one.
    /// </summary>
    public static async Task<int> Show(Window? owner, string title, string message, AlertKind kind, params string[] buttons)
    {
        owner ??= Shell.MainWindow.Current;

        var result = -1;
        var window = new Window
        {
            Title = string.Empty,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            MaxWidth = 520
        };

        var glyph = kind switch
        {
            AlertKind.Warning => "Warning",
            AlertKind.Error => "Warning",
            AlertKind.Question => "Question",
            _ => "About"
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == buttons.Length - 1)
            {
                button.Classes.Add("accent");
                button.IsDefault = true;
            }

            if (i == 0 && buttons.Length > 1) button.IsCancel = true;

            button.Click += (_, _) =>
            {
                result = index;
                window.Close();
            };
            row.Children.Add(button);
        }

        var icon = new PathIcon
        {
            Data = Glyphs.Get(glyph),
            Width = 34,
            Height = 34,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 16, 0)
        };
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable(kind == AlertKind.Error ? "Cc.Danger" : "Cc.Accent"));

        var body = new StackPanel { Spacing = 8, MaxWidth = 400 };
        body.Children.Add(new TextBlock { Text = title, Classes = { "h2" }, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(message))
            body.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });

        var top = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(body);

        var layout = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 18 };
        layout.Children.Add(top);
        layout.Children.Add(row);
        window.Content = layout;

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
                e.Handled = true;
            }
        };

        if (owner != null && owner.IsVisible) await window.ShowDialog(owner);
        else
        {
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await closed.Task;
        }

        return result;
    }
}
