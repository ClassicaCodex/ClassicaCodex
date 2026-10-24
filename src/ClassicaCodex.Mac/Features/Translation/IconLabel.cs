using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// A glyph and a few words, as a button's content, whose words can change -
/// "Compare (write first)" becoming "Compare published", "Translate with
/// Gemini" becoming "Translate Remaining 40 Line(s)".
///
/// After the words change it asks the button's presenter to lay it out
/// again, once the current layout has finished. Without that, a label that
/// grows in the same layout passes as a long list settles its row heights -
/// Create Translation fills two lists of a whole work and relabels its
/// button in one go - can be measured for its new words but left arranged
/// at the width of its old ones, clipped, with nothing still marked as
/// needing layout.
/// </summary>
internal sealed class IconLabel : StackPanel
{
    private readonly PathIcon _icon;
    private readonly TextBlock _text;

    public IconLabel(string glyph, string text)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        _icon = Glyphs.Icon(glyph, 14);
        _text = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_icon);
        Children.Add(_text);
    }

    public string Text
    {
        get => _text.Text ?? string.Empty;
        set
        {
            if (_text.Text == value) return;
            _text.Text = value;
            RelayoutLater();
        }
    }

    public void Set(string glyph, string text)
    {
        _icon.Data = Glyphs.Get(glyph);
        Text = text;
    }

    private void RelayoutLater() =>
        Dispatcher.UIThread.Post(() => this.GetVisualParent<Layoutable>()?.InvalidateArrange(), DispatcherPriority.Background);
}
