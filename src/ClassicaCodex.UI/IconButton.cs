namespace ClassicaCodex.UI;

/// <summary>
/// A toolbar button that is its icon: no border, no raised face, nothing
/// drawn but the image and a hover tint behind it.
///
/// It exists as its own type so ReadingTheme can style it differently from
/// an ordinary Button - the same way GraphCanvas and SyncListView are
/// matched separately there. The theme's normal button styling is what makes
/// a plain Button unsuitable here: it forces FlatStyle.Standard in light mode
/// for the raised look, and a visible border in dark mode, and either way the
/// result is a box drawn around an icon that is already a self-contained
/// tile. Two nested rectangles, one of them redundant.
///
/// The icons this carries are detailed illustrations rather than flat
/// glyphs. Letting one fill its button - rather than sitting at 16px beside
/// a text label - is the difference between reading as a vase and reading as
/// a smudge.
/// </summary>
public class IconButton : Button
{
    public IconButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Text = string.Empty;
        ImageAlign = ContentAlignment.MiddleCenter;

        // Without this the control paints its own square of BackColor before
        // the image, which shows as a hard edge wherever the icon's rounded
        // corners leave the button's square ones exposed.
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }
}
