using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// A button in the arcade's control bar: a painted icon and its words, and
/// for the actions with a keyboard way in, a key cap showing which key.
///
/// Two shapes. A primary action - begin, check the passage, ask the oracle -
/// is a wide button with the icon beside its words, as the Windows buttons
/// are. The secondary ones - the library, the journal, the bestiary, a new
/// adventure, the sound - are toolbar items with the icon above a short
/// caption, the way a Mac toolbar lays out "icon and text", so the whole row
/// fits on one line at the window's narrowest instead of wrapping as the
/// Windows flow panel does.
///
/// A wrapper rather than a Button subclass so the application's Button
/// styles (accent, toolbar) apply to it unchanged.
/// </summary>
internal sealed class ArcadeAction
{
    private readonly Image _icon;
    private readonly TextBlock _label;
    private readonly Border? _cap;
    private readonly TextBlock? _capText;

    public Button Button { get; }

    public ArcadeAction(string label, Bitmap? icon, Action action, bool primary, string? keyCap = null)
    {
        _label = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

        if (primary)
        {
            _icon = BronzeIcons.Picture(icon, 22);
            _icon.VerticalAlignment = VerticalAlignment.Center;

            // Outlined in the button's own ink, so the cap reads on the
            // bronze of an accent button and on a plain one, in either
            // appearance, without a colour of its own to get wrong.
            _capText = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _cap = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 0, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _capText
            };
            _cap.Bind(Border.BorderBrushProperty, _capText.GetObservable(TextBlock.ForegroundProperty));

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(_icon);
            content.Children.Add(_label);
            content.Children.Add(_cap);

            Button = new Button
            {
                Content = content,
                MinHeight = 38,
                Padding = new Thickness(10, 6, 12, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            KeyCap = keyCap;
        }
        else
        {
            _icon = BronzeIcons.Picture(icon, 28);
            _icon.HorizontalAlignment = HorizontalAlignment.Center;
            _label.FontSize = 11;
            _label.HorizontalAlignment = HorizontalAlignment.Center;

            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(_icon);
            content.Children.Add(_label);

            Button = new Button
            {
                Content = content,
                Classes = { "toolbar" },
                Height = double.NaN,
                MinWidth = 64,
                Padding = new Thickness(8, 5, 8, 4)
            };
        }

        Button.Click += (_, _) => action();
    }

    public string Label
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    public Bitmap? Icon
    {
        set => _icon.Source = value;
    }

    /// <summary>The key shown in the cap beside a primary action's words; null hides the cap.</summary>
    public string? KeyCap
    {
        set
        {
            if (_cap == null || _capText == null) return;
            _capText.Text = value;
            _cap.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public bool IsVisible
    {
        get => Button.IsVisible;
        set => Button.IsVisible = value;
    }

    public bool IsEnabled
    {
        get => Button.IsEnabled;
        set => Button.IsEnabled = value;
    }

    /// <summary>What the Windows button gives a screen reader as its description; here the tooltip as well.</summary>
    public string? Tip
    {
        set
        {
            ToolTip.SetTip(Button, value);
            Avalonia.Automation.AutomationProperties.SetHelpText(Button, value);
        }
    }
}
