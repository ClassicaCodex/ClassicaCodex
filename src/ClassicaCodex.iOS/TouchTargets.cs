using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Styling;

namespace ClassicaCodex.iOS;

/// <summary>
/// Makes the controls big enough for a finger. The shared theme sets them for a
/// pointer, and over a third were under 30 points, where Apple asks for 44.
/// Done here as styles, added after the theme so they win, which reaches every
/// window at once and leaves the Mac's sizes alone. Tick boxes and fields get a
/// little less, because a form of them would otherwise run on too long.
/// </summary>
internal static class TouchTargets
{
    private const double Tall = 44;
    private const double Field = 40;

    public static void Install(Styles styles)
    {
        styles.Add(Labelled(x => x.OfType<Button>()));
        styles.Add(Labelled(x => x.OfType<ToggleButton>()));
        styles.Add(AtLeast(x => x.OfType<ComboBox>(), Tall));
        styles.Add(AtLeast(x => x.OfType<TabItem>(), Tall));
        styles.Add(AtLeast(x => x.OfType<CheckBox>(), Field));
        styles.Add(AtLeast(x => x.OfType<RadioButton>(), Field));
        styles.Add(AtLeast(x => x.OfType<TextBox>(), Field));
        styles.Add(AtLeast(x => x.OfType<NumericUpDown>(), Field));

        styles.Add(new Style(x => x.OfType<ButtonSpinner>().Descendant().OfType<RepeatButton>())
        {
            Setters =
            {
                new Setter(Layoutable.MinWidthProperty, Tall),
                new Setter(Layoutable.MinHeightProperty, Field)
            }
        });
    }

    private static Style AtLeast(Func<Selector?, Selector> selector, double height) => new(selector)
    {
        Setters = { new Setter(Layoutable.MinHeightProperty, height) }
    };

    /// <summary>A button, with its label kept in the middle of the extra height.</summary>
    private static Style Labelled(Func<Selector?, Selector> selector) => new(selector)
    {
        Setters =
        {
            new Setter(Layoutable.MinHeightProperty, Tall),
            new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center)
        }
    };
}
