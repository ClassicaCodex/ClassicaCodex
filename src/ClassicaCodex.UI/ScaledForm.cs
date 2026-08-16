namespace ClassicaCodex.UI;

/// <summary>
/// A Form that scales with the system text size.
///
/// Every window in this application derives from this rather than from Form directly.
/// The scaling has to be established before any control is created or positioned, and a
/// base constructor runs before the derived one's body - which makes inheritance a more
/// reliable place for it than a call each constructor has to remember to make first.
///
/// See <see cref="DpiScaling"/> for what the scaling actually does and why absolute
/// coordinates need it.
/// </summary>
public class ScaledForm : Form
{
    protected ScaledForm() => DpiScaling.UseDesignFontScaling(this);
}
