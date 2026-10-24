using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.Mac.Features.Reactions;

/// <summary>
/// Who a speaker is, and - the part that matters - whether they existed.
///
/// Opened by clicking a face or a name in the debate. The first line under the
/// name is the answer to the question a reader should be asking, in as many
/// words as it takes to be unambiguous: either this person is invented, or
/// they are real and every line they speak here carries the ancient reference
/// it came from. An invented speaker's line is set in the warning colour, so
/// the two answers cannot be mistaken for each other at a glance.
/// </summary>
internal sealed class CriticCardWindow : Window
{
    private const double PortraitSize = 96;

    public CriticCardWindow(AncientCritic critic)
    {
        Title = critic.Name;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var portrait = new CriticPortrait(critic, PortraitSize)
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 18, 0)
        };

        var nameRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock
        {
            Text = critic.Name,
            Classes = { "h1" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        name.Bind(TextBlock.FontFamilyProperty, name.GetResourceObservable("Cc.DisplayFont"));
        nameRow.Children.Add(name);
        if (critic.Kind == CriticKind.Historical)
        {
            var badge = ReactionsWindow.RealPersonBadge(11.5);
            badge.VerticalAlignment = VerticalAlignment.Center;
            nameRow.Children.Add(badge);
        }

        var kind = new TextBlock
        {
            Text = critic.Kind == CriticKind.Historical
                ? "A real person. Everything they say here is a paraphrase of something "
                  + "they actually wrote, and each of their turns carries the ancient "
                  + "reference so you can check it."
                : "Invented for this debate. Nobody of this name said any of this. They are "
                  + "here to voice the concerns of people who really did watch, read and "
                  + "argue about this work, and who left nothing in writing.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        if (critic.Kind != CriticKind.Historical) kind.Classes.Add("warning");

        var heading = new StackPanel();
        heading.Children.Add(nameRow);
        heading.Children.Add(kind);

        var top = new DockPanel();
        DockPanel.SetDock(portrait, Dock.Left);
        top.Children.Add(portrait);
        top.Children.Add(heading);

        var facts = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 18, 0, 0)
        };
        var row = 0;
        foreach (var (label, value) in new[]
                 {
                     ("Era", critic.Era),
                     ("Active", critic.FloruitLabel),
                     ("Place", critic.Place),
                     ("Trade", critic.Role)
                 })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var key = new TextBlock { Text = label.ToUpperInvariant(), Classes = { "section" }, Margin = new Thickness(0, 3, 14, 3), VerticalAlignment = VerticalAlignment.Top };
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 3) };
            Grid.SetRow(key, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            facts.Children.Add(key);
            facts.Children.Add(text);
            row++;
        }

        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(top);
        body.Children.Add(facts);

        if (!string.IsNullOrWhiteSpace(critic.Sketch))
        {
            body.Children.Add(new Border { Classes = { "rule" }, Margin = new Thickness(0, 14, 0, 12) });
            var sketch = new SelectableTextBlock
            {
                Text = critic.Sketch,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 20
            };
            sketch.Bind(TextBlock.FontFamilyProperty, sketch.GetResourceObservable("Cc.TranslationFont"));
            body.Children.Add(sketch);
        }

        if (!string.IsNullOrWhiteSpace(critic.Tastes))
        {
            body.Children.Add(new TextBlock
            {
                Text = "Cares about: " + critic.Tastes,
                TextWrapping = TextWrapping.Wrap,
                Classes = { "muted" },
                Margin = new Thickness(0, 10, 0, 0)
            });
        }

        var close = new Button
        {
            Content = "Close",
            Classes = { "accent" },
            IsDefault = true,
            IsCancel = true,
            MinWidth = 84,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        close.Click += (_, _) => Close();
        body.Children.Add(close);

        Content = new Border { Padding = new Thickness(22, 20, 22, 18), Child = body };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                Close();
                e.Handled = true;
            }
        };
    }
}
